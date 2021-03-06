﻿using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using VkNet.Enums.Filters;
using VkNet.Enums.SafetyEnums;
using VkNet.Exception;

namespace VkNet.Utils
{
	/// <summary>
	/// Браузер, через который производится сетевое взаимодействие с ВКонтакте.
	/// Сетевое взаимодействие выполняется с помощью HttpWebRequest
	/// </summary>
	public class Browser : IBrowser
	{
		/// <summary>
		/// Прокси сервер
		/// </summary>
		public IWebProxy Proxy { get; set; }

		/// <summary>
		/// Получение json по url-адресу
		/// </summary>
		/// <param name="methodUrl">Адрес получения json</param>
		/// <param name="parameters">Параметры метода api</param>
		/// <returns>Строка в формате json</returns>
		public string GetJson(string methodUrl, IEnumerable<KeyValuePair<string, string>> parameters)
		{
			return WebCall.PostCall(methodUrl, parameters, Proxy).Response;
		}

		/// <summary>
		/// Загружает файл на заданный Uri
		/// </summary>
		/// <param name="uploadUrl">Адрес для загрузки</param>
		/// <param name="path">Путь к файлу</param>
		/// <returns>Cтрока, используемая далее в Vk API</returns>
		[NotNull]
		public static string UploadFile([NotNull] string uploadUrl, [NotNull] string path)
		{
			using (var client = new WebClient())
			{
				var uploadedFile = client.UploadFile(uploadUrl, path);
				var answer = Encoding.UTF8.GetString(uploadedFile, 0, uploadedFile.Length);

				var json = JObject.Parse(answer);

				var rawResponse = json["file"];
				return new VkResponse(rawResponse) {RawJson = answer};
			}
		}

		/// <summary>
		/// Скачивает файл по заданному Uri
		/// </summary>
		/// <param name="url">Uri для скачки</param>
		/// <param name="path">Путь сохранения файла</param>
		public static void DownloadFile([NotNull] string url, [NotNull] string path)
		{
			using (var client = new WebClient())
			{
				client.DownloadFile(url, path);
			}
		}

#if false /// <summary>
/// Асинхронное получение json по url-адресу
/// </summary>
/// <param name="url">Адрес получения json</param>
/// <returns>Строка в формате json</returns>
        public async Task<string> GetJsonAsync(string url)
        {
            // todo refactor this shit
            var separatorPosition = url.IndexOf('?');
            string methodUrl = separatorPosition < 0 ? url : url.Substring(0, separatorPosition);
            string parameters = separatorPosition < 0 ? string.Empty : url.Substring(separatorPosition + 1);

            return await WebCall.PostCallAsync(url, parameters);
        }
#endif

		/// <summary>
		/// Авторизация на сервере ВК
		/// </summary>
		/// <param name="appId">Идентификатор приложения</param>
		/// <param name="email">Логин - телефон или эл. почта</param>
		/// <param name="password">Пароль</param>
		/// <param name="settings">Уровень доступа приложения</param>
		/// <param name="code">Код двухфакторной авторизации</param>
		/// <param name="captchaSid">Идентификатор капчи</param>
		/// <param name="captchaKey">Текст капчи</param>
		/// <returns>Информация об авторизации приложения</returns>
		public VkAuthorization Authorize(ulong appId, string email, string password, Settings settings,
			Func<string> code = null, long? captchaSid = null, string captchaKey = null)
		{
			var authorizeUrl = CreateAuthorizeUrlFor(appId, settings, Display.Wap);
			var authorizeUrlResult = WebCall.MakeCall(authorizeUrl, Proxy);

			if (authorizeUrlResult.ResponseUrl.ToString()
				.StartsWith("https://oauth.vk.com/blank.html#access_token=", StringComparison.Ordinal))
			{
				return EndAuthorize(authorizeUrlResult, Proxy);
			}

			// Заполнить логин и пароль
			var loginForm = WebForm.From(authorizeUrlResult)
				.WithField("email")
				.FilledWith(email)
				.And()
				.WithField("pass")
				.FilledWith(password);
			if (captchaSid.HasValue)
			{
				loginForm.WithField("captcha_sid")
					.FilledWith(captchaSid.Value.ToString())
					.WithField("captcha_key")
					.FilledWith(captchaKey);
			}

			var loginFormPostResult = WebCall.Post(loginForm, Proxy);

			// Заполнить код двухфакторной авторизации
			var isOAuthBlank = WebForm.IsOAuthBlank(loginFormPostResult);

			if (code == null)
			{
				return EndAuthorize(loginFormPostResult, Proxy);
			}

			if (isOAuthBlank)
			{
				return EndAuthorize(loginFormPostResult, Proxy);
			}

			var codeForm = WebForm.From(loginFormPostResult)
				.WithField("code")
				.FilledWith(code.Invoke());
			loginFormPostResult = WebCall.Post(codeForm, Proxy);


			return EndAuthorize(loginFormPostResult, Proxy);
		}

		/// <summary>
		/// Выполняет обход ошибки валидации: https://vk.com/dev/need_validation
		/// </summary>
		/// <param name="validateUrl">Адрес страницы валидации</param>
		/// <param name="phoneNumber">Номер телефона, который необходимо ввести на странице валидации</param>
		/// <returns>Информация об авторизации приложения.</returns>
		public VkAuthorization Validate(string validateUrl, string phoneNumber)
		{
			if (string.IsNullOrWhiteSpace(validateUrl))
			{
				throw new ArgumentException("Не задан адрес валидации!");
			}
			if (string.IsNullOrWhiteSpace(phoneNumber))
			{
				throw new ArgumentException("Не задан номер телефона!");
			}

			var validateUrlResult = WebCall.MakeCall(validateUrl, Proxy);
			var codeForm = WebForm.From(validateUrlResult)
				.WithField("code")
				.FilledWith(phoneNumber.Substring(1, 8));
			var codeFormPostResult = WebCall.Post(codeForm, Proxy);

			return EndAuthorize(codeFormPostResult, Proxy);
		}


		private VkAuthorization EndAuthorize(WebCallResult result, IWebProxy webProxy = null)
		{
			var authorization = VkAuthorization.From(result.ResponseUrl);
			if (authorization.CaptchaId.HasValue)
			{
				throw new CaptchaNeededException(authorization.CaptchaId.Value,
					"http://api.vk.com/captcha.php?sid=" + authorization.CaptchaId.Value);
			}

			if (!authorization.IsAuthorizationRequired)
			{
				return authorization;
			}

			// Отправить данные
			var authorizationForm = WebForm.From(result);
			var authorizationFormPostResult = WebCall.Post(authorizationForm, webProxy);

			return VkAuthorization.From(authorizationFormPostResult.ResponseUrl);
		}

		/// <summary>
		/// Построить URL для авторизации.
		/// </summary>
		/// <param name="appId">Идентификатор приложения.</param>
		/// <param name="settings">Настройки прав доступа.</param>
		/// <param name="display">Вид окна авторизации.</param>
		/// <returns>Возвращает Uri для авторизации</returns>
		[NotNull]
		public static string CreateAuthorizeUrlFor(ulong appId, [NotNull] Settings settings, [NotNull] Display display)
		{
			var builder = new StringBuilder("https://oauth.vk.com/authorize?");

			builder.AppendFormat("client_id={0}&", appId);
			builder.AppendFormat("scope={0}&", settings);
			builder.Append("redirect_uri=https://oauth.vk.com/blank.html&");
			builder.AppendFormat("display={0}&", display);
			builder.Append("response_type=token");

			return builder.ToString();
		}
	}
}