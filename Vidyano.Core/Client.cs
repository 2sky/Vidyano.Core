using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vidyano.Common;
using Vidyano.ViewModel;
using Vidyano.ViewModel.Actions;

namespace Vidyano
{
    /// <summary>
    /// Connects to a Vidyano backend.
    /// </summary>
    public sealed class Client : NotifyableBase
    {
        #region Fields

        private static readonly HashSet<Type> defaultConverterTypes = new HashSet<Type>(new[]
            {
                typeof(byte), typeof(sbyte), typeof(char), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(bool), typeof(Guid), typeof(string),
                typeof(byte?), typeof(sbyte?), typeof(char?), typeof(short?), typeof(ushort?), typeof(int?), typeof(uint?), typeof(long?), typeof(ulong?), typeof(float?), typeof(double?), typeof(decimal?), typeof(bool?), typeof(Guid?), typeof(byte[])
            });

        private static readonly Dictionary<Type, object> defaultValues = new Dictionary<Type, object>();

        private static readonly Dictionary<string, NoInternetMessage> noInternetMessages = new Dictionary<string, NoInternetMessage>
        {
            { "en", new NoInternetMessage("Unable to connect to the server.", "Please check your internet connection settings and try again.", "Try again") },
            { "ar", new NoInternetMessage("غير قادر على الاتصال بالخادم", "يرجى التحقق من إعدادات الاتصال بإنترنت ثم حاول مرة أخرى", "حاول مرة أخرى") },
            { "bg", new NoInternetMessage("Не може да се свърже със сървъра", "Проверете настройките на интернет връзката и опитайте отново", "Опитайте отново") },
            { "ca", new NoInternetMessage("No es pot connectar amb el servidor", "Si us plau aturi les seves escenes de connexió d'internet i provi una altra vegada", "Provi una altra vegada") },
            { "cs", new NoInternetMessage("Nelze se připojit k serveru", "Zkontrolujte nastavení připojení k Internetu a akci opakujte", "Zkuste to znovu") },
            { "da", new NoInternetMessage("Kunne ikke oprettes forbindelse til serveren", "Kontroller indstillingerne for internetforbindelsen, og prøv igen", "Prøv igen") },
            { "nl", new NoInternetMessage("Kan geen verbinding maken met de server", "Controleer de instellingen van uw internet-verbinding en probeer opnieuw", "Opnieuw proberen") },
            { "et", new NoInternetMessage("Ei saa ühendust serveriga", "Palun kontrollige oma Interneti-ühenduse sätteid ja proovige uuesti", "Proovi uuesti") },
            { "fa", new NoInternetMessage("قادر به اتصال به سرویس دهنده", "لطفاً تنظیمات اتصال اینترنت را بررسی کرده و دوباره سعی کنید", "دوباره امتحان کن") },
            { "fi", new NoInternetMessage("Yhteyttä palvelimeen", "Tarkista internet-yhteysasetukset ja yritä uudelleen", "Yritä uudestaan") },
            { "fr", new NoInternetMessage("Impossible de se connecter au serveur", "S'il vous plaît vérifier vos paramètres de connexion internet et réessayez", "Réessayez") },
            { "de", new NoInternetMessage("Keine Verbindung zum Server herstellen", "Überprüfen Sie die Einstellungen für die Internetverbindung und versuchen Sie es erneut", "Wiederholen") },
            { "el", new NoInternetMessage("Δεν είναι δυνατή η σύνδεση με το διακομιστή", "Ελέγξτε τις ρυθμίσεις σύνδεσης στο internet και προσπαθήστε ξανά", "Δοκίμασε ξανά") },
            { "ht", new NoInternetMessage("Pat kapab pou li konekte li pou sèvè a", "Souple tcheke ou paramètres kouche sou entènèt Et eseye ankò", "eseye ankò") },
            { "he", new NoInternetMessage("אין אפשרות להתחבר לשרת", "נא בדוק את הגדרות החיבור לאינטרנט ונסה שוב", "נסה שוב") },
            { "hi", new NoInternetMessage("सर्वर से कनेक्ट करने में असमर्थ", "कृपया अपना इंटरनेट कनेक्शन सेटिंग्स की जाँच करें और पुन: प्रयास करें", "फिर कोशिश करो") },
            { "hu", new NoInternetMessage("Nem lehet kapcsolódni a szerverhez", "Kérjük, ellenőrizze az internetes kapcsolat beállításait, és próbálja újra", "próbáld újra") },
            { "id", new NoInternetMessage("Tidak dapat terhubung ke server", "Silakan periksa setelan sambungan internet Anda dan coba lagi", "Coba lagi") },
            { "it", new NoInternetMessage("Impossibile connettersi al server", "Si prega di controllare le impostazioni della connessione internet e riprovare", "Riprova") },
            { "ja", new NoInternetMessage("サーバーに接続できません。", "インターネット接続設定を確認して、やり直してください。", "もう一度やり直してください") },
            { "ko", new NoInternetMessage("서버에 연결할 수 없습니다.", "인터넷 연결 설정을 확인 하 고 다시 시도 하십시오", "다시 시도") },
            { "lv", new NoInternetMessage("Nevar izveidot savienojumu ar serveri", "Lūdzu, pārbaudiet interneta savienojuma iestatījumus un mēģiniet vēlreiz", "mēģini vēlreiz") },
            { "lt", new NoInternetMessage("Nepavyko prisijungti prie serverio", "Patikrinkite interneto ryšio parametrus ir bandykite dar kartą", "pabandyk dar kartą") },
            { "no", new NoInternetMessage("Kan ikke koble til serveren", "Kontroller innstillingene for Internett-tilkoblingen og prøv igjen", "prøv igjen") },
            { "pl", new NoInternetMessage("Nie można połączyć się z serwerem", "Proszę sprawdzić ustawienia połączenia internetowego i spróbuj ponownie", "Próbuj ponownie") },
            { "pt", new NoInternetMessage("Incapaz de conectar ao servidor", "Por favor, verifique suas configurações de conexão de internet e tente novamente", "Tentar novamente") },
            { "ro", new NoInternetMessage("Imposibil de conectat la server", "Vă rugăm să verificaţi setările de conexiune la internet şi încercaţi din nou", "încearcă din nou") },
            { "ru", new NoInternetMessage("Не удается подключиться к серверу", "Пожалуйста, проверьте параметры подключения к Интернету и повторите попытку", "Повторить") },
            { "sk", new NoInternetMessage("Nedá sa pripojiť k serveru", "Skontrolujte nastavenie internetového pripojenia a skúste to znova", "skús znova") },
            { "sl", new NoInternetMessage("Ne morem se povezati s strežnikom", "Preverite nastavitve internetne povezave in poskusite znova", "poskusi znova") },
            { "es", new NoInternetMessage("No se puede conectar al servidor", "Por favor, compruebe la configuración de conexión a internet e inténtelo de nuevo", "Vuelve a intentarlo") },
            { "sv", new NoInternetMessage("Det gick inte att ansluta till servern", "Kontrollera inställningarna för Internetanslutningen och försök igen", "Försök igen") },
            { "th", new NoInternetMessage("สามารถเชื่อมต่อกับเซิร์ฟเวอร์", "กรุณาตรวจสอบการตั้งค่าการเชื่อมต่ออินเทอร์เน็ตของคุณ และลองอีกครั้ง", "ลองอีกครั้ง") },
            { "tr", new NoInternetMessage("Sunucuya bağlantı kurulamıyor", "Lütfen Internet bağlantı ayarlarınızı denetleyin ve yeniden deneyin", "Yeniden Deneyin") },
            { "uk", new NoInternetMessage("Не вдалося підключитися до сервера", "Перевірте параметри підключення до Інтернету та повторіть спробу", "Спробуй ще раз") },
            { "vi", new NoInternetMessage("Không thể kết nối đến máy chủ", "Hãy kiểm tra cài đặt kết nối internet của bạn và thử lại", "Thử lại") },
        };

        private readonly HttpClient httpClient;

        private PersistentObject _Application, _Session;
        private bool _IsBusy, _IsConnected, _IsUsingDefaultCredentials;
        private KeyValueList<string, string> _Messages;

        #endregion

        #region Constructors

        public Client(HttpClient client = null)
        {
            Current = this;

            httpClient = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        }

        #endregion

        #region Properties

        public event Action<Exception> OnException;

        public static Client Current { get; private set; }

        public PersistentObject Application
        {
            get => _Application;
            set => SetProperty(ref _Application, value);
        }

        public PersistentObject Session
        {
            get => _Session;
            set => SetProperty(ref _Session, value);
        }

        public string Uri { get; set; }

        public bool IsBusy
        {
            get => _IsBusy;
            set => SetProperty(ref _IsBusy, value);
        }

        public bool IsConnected
        {
            get => _IsConnected;
            private set => SetProperty(ref _IsConnected, value);
        }

        public bool IsUsingDefaultCredentials
        {
            get => _IsUsingDefaultCredentials;
            internal set => SetProperty(ref _IsUsingDefaultCredentials, value);
        }

        public KeyValueList<string, string> Messages
        {
            get => _Messages ?? new KeyValueList<string, string>(new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()));
            private set => _Messages = value;
        }

        public Hooks Hooks { get; set; } = new Hooks();

        public Action<string, JObject> LogPosts { get; set; }

        #endregion

        #region Internal Properties

        internal bool IsMobile { get; set; }

        internal IReadOnlyDictionary<string, ActionBase.Definition> Actions { get; private set; }

        internal string AuthToken { get; set; }

        /// <summary>
        /// Is used on the Authorization header
        /// </summary>
        internal AuthenticationHeaderValue AuthorizationHeader { get; set; }

        public string User { get; private set; }

        public object UserPicture { get; private set; }

        #endregion

        #region Service Call Methods

        internal void CancelPendingServiceCalls()
        {
            httpClient.CancelPendingRequests();
        }

        public async Task<ClientData> GetClientData(string environment = null)
        {
            try
            {
                IsBusy = true;

                return new ClientData(JObject.Parse(await httpClient.GetStringAsync(new Uri(Uri) + "GetClientData?environment=" + (environment ?? Hooks.Environment)).ConfigureAwait(false)));
            }
            catch
            {
                // Ignore no internet/service
                return null;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private JObject CreateData(string user = null, string authToken = null)
        {
            var data = new JObject();

            if (AuthorizationHeader == null)
            {
                data["userName"] = user ?? User;
                data["authToken"] = authToken ?? AuthToken;
            }

            data["environment"] = Hooks.Environment;
            data["isMobile"] = IsMobile;
            var uniqueId = Hooks.UniqueId;
            if (!string.IsNullOrEmpty(uniqueId))
            {
                data["uniqueId"] = !IsUsingDefaultCredentials ? "rsa-" + uniqueId : null;
                data["timestamp"] = !IsUsingDefaultCredentials ? Hooks.GetSignedTimeStamp() : null;
            }
            data["requestedExpiration"] = ToServiceString(DateTimeOffset.Now.AddYears(1));

            if (Session != null)
                data["session"] = Session.ToServiceObject();

            Hooks.OnCreateData(data);

            return data;
        }

        private async Task<JObject> PostAsync(string method, JObject data)
        {
            JObject Log(JObject result)
            {
                LogPosts?.Invoke(method, result);

                return result;
            }

            HttpResponseMessage responseMsg;
            try
            {
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(Uri) + method) { Content = new StringContent(data.ToString(Formatting.None)) };
                if (AuthorizationHeader != null)
                    requestMessage.Headers.Authorization = AuthorizationHeader;
                responseMsg = await httpClient.SendAsync(requestMessage).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return Log(new JObject(new JProperty("exception", "Timeout: request took longer than " + httpClient.Timeout)));
            }
            catch (Exception responseEx)
            {
                return Log(new JObject(new JProperty("exception", GetNoInternetMessage().Message + "\n\nException: " + responseEx)));
            }

            if (!responseMsg.IsSuccessStatusCode)
                return Log(new JObject(new JProperty("exception", "error, status: " + responseMsg.StatusCode)));

            var content = await responseMsg.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(content))
                return Log(new JObject(new JProperty("exception", GetNoInternetMessage().Title)));

            var response = JObject.Parse(content);

            var ex = (string)response["exception"];
            if (!string.IsNullOrEmpty(ex) && ex == "Session expired")
            {
                if (IsUsingDefaultCredentials)
                {
                    data.Remove("password");
                    data.Remove("authToken");

                    responseMsg = await httpClient.PostAsync(new Uri(Uri) + method, new StringContent(data.ToString(Formatting.None))).ConfigureAwait(false);
                    response = JObject.Parse(await responseMsg.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
                else
                {
                    IsConnected = false;
                    // TODO: Redirect to sign in
                }
            }

            return Log(response);
        }

        public Task<PersistentObject> SignInUsingAccessTokenAsync(string accessToken, string serviceProvider = "Microsoft")
        {
            AuthorizationHeader = null;

            return SignInAsync(null, null, accessToken: accessToken, serviceProvider: serviceProvider);
        }

        public Task<PersistentObject> SignInUsingCredentialsAsync(string user, string password)
        {
            AuthorizationHeader = null;

            return SignInAsync(user, password);
        }

        public Task<PersistentObject> SignInUsingAuthTokenAsync(string user, string token)
        {
            AuthorizationHeader = null;

            return SignInAsync(user, null, token);
        }

        public Task<PersistentObject> SignInUsingAuthorizationHeaderAsync(AuthenticationHeaderValue authorizationHeader)
        {
            AuthorizationHeader = authorizationHeader ?? throw new ArgumentNullException(nameof(authorizationHeader));

            return SignInAsync(null, null);
        }

        private async Task<PersistentObject> SignInAsync(string user, string password, string token = null, string accessToken = null, string serviceProvider = null)
        {
            if (Hooks == null)
                throw new InvalidOperationException("Need to set Hooks property first.");

            try
            {
                IsBusy = true;

                var data = CreateData(user, token);
                if (string.IsNullOrEmpty(accessToken))
                {
                    if (password != null)
                    {
                        data["password"] = password;
                        data.Remove("authToken");
                    }
                }
                else
                {
                    data.Remove("userName");
                    data.Remove("authToken");
                    data["accessToken"] = accessToken;
                    data["serviceProvider"] = serviceProvider;
                }

                var response = await PostAsync("GetApplication", data).ConfigureAwait(false);

                var ex = (string)response["exception"] ?? (string)response["ExceptionMessage"];
                if (!string.IsNullOrEmpty(ex))
                    throw new Exception(ex);

                var po = Hooks.OnConstruct(this, (JObject)response["application"]);
                if (po.FullTypeName == "Vidyano.Error" || !string.IsNullOrEmpty(po.Notification))
                    throw new Exception(po.Notification);

                User = (string)response["userName"] ?? user;
                UserPicture = await Hooks.UserPictureFromUrl((string)response["userPicture"]).ConfigureAwait(false);

                AuthToken = (string)response["authToken"];

                Application = po;

                var cultureInfo = new CultureInfo(Application["Culture"].ValueDirect);
                CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
                CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

                Messages = new KeyValueList<string, string>(Application.Queries["ClientMessages"].ToDictionary(item => (string)item["Key"], item => (string)item["Value"]), true);
                Actions = Application.Queries["Actions"].ToDictionary(item => (string)item["Name"], item => new ActionBase.Definition
                {
                    Name = (string)item["Name"],
                    DisplayName = (string)item["DisplayName"],
                    IsPinned = (bool)item["IsPinned"],
                    RefreshQueryOnCompleted = (bool)item["RefreshQueryOnCompleted"],
                    Offset = (int)item["Offset"],
                    Options = ((string)item["Options"] ?? string.Empty).Trim().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(str => str.Trim()).ToArray(),
                    SelectionRule = ExpressionParser.Get((string)item["SelectionRule"])
                });

                await UpdateSession(response).ConfigureAwait(false);

                var bulkEdit = Actions["BulkEdit"];
                bulkEdit.SelectionRule = ExpressionParser.Get("=1");
            }
            catch (Exception e)
            {
                OnException?.Invoke(e);

                throw;
            }
            finally
            {
                IsBusy = false;
            }

            IsConnected = true;

            await Hooks.OnInitialized().ConfigureAwait(false);

            return Application;
        }

        public async Task<PersistentObject> GetPersistentObjectAsync(string id, string objectId = null, PersistentObject parent = null, bool isNew = false)
        {
            try
            {
                IsBusy = true;

                var data = CreateData();
                data["persistentObjectTypeId"] = id;
                data["objectId"] = objectId;
                if (parent != null)
                    data["parent"] = parent.ToServiceObject();
                if (isNew)
                    data["isNew"] = true;

                var response = await PostAsync("GetPersistentObject", data).ConfigureAwait(false);

                var ex = (string)response["exception"] ?? (string)response["ExceptionMessage"];
                if (!string.IsNullOrEmpty(ex))
                    throw new Exception(ex);

                AuthToken = (string)response["authToken"];
                await UpdateSession(response).ConfigureAwait(false);

                var result = (JObject)response["result"];
                var po = result != null ? Hooks.OnConstruct(this, result) : null;

                if (po != null && po.FullTypeName == "Vidyano.Error")
                    throw new Exception(po.Notification);

                return po;
            }
            catch (Exception e)
            {
                OnException?.Invoke(e);

                throw;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<Query> GetQueryAsync(string id, string filterName = null)
        {
            try
            {
                IsBusy = true;

                var data = CreateData();
                data["id"] = id;
                data["filterName"] = filterName;

                var response = await PostAsync("GetQuery", data).ConfigureAwait(false);

                var ex = (string)response["exception"] ?? (string)response["ExceptionMessage"];
                if (!string.IsNullOrEmpty(ex))
                    throw new Exception(ex);

                AuthToken = (string)response["authToken"];
                await UpdateSession(response).ConfigureAwait(false);

                var result = (JObject)response["query"];
                return result != null ? Hooks.OnConstruct(this, result, null, false) : null;
            }
            catch (Exception e)
            {
                OnException?.Invoke(e);

                throw;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<JObject> ExecuteQueryAsync(Query query, PersistentObject parent = null, string filterName = null, bool asLookup = false)
        {
            try
            {
                IsBusy = true;

                var data = CreateData();
                data["query"] = query.ToServiceObject();
                data["parent"] = parent?.ToServiceObject();
                data["filterName"] = filterName;
                data["asLookup"] = asLookup;

                var response = await PostAsync("ExecuteQuery", data).ConfigureAwait(false);

                var ex = (string)response["exception"] ?? (string)response["ExceptionMessage"];
                if (!string.IsNullOrEmpty(ex))
                    throw new Exception(ex);

                AuthToken = (string)response["authToken"];
                await UpdateSession(response).ConfigureAwait(false);

                return (JObject)response["result"];
            }
            catch (Exception e)
            {
                OnException?.Invoke(e);

                throw;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<Tuple<Stream, string>> GetStreamAsync(PersistentObject registeredStream) //, string action = null, PersistentObject parent = null, Query query = null, QueryResultItem[] selectedItems = null, Dictionary<string, string> parameters = null)
        {
            try
            {
                IsBusy = true;

                var data = CreateData();
                if (registeredStream != null)
                    data["id"] = registeredStream.ObjectId;

                var req = new MultipartFormDataContent("VidyanoBoundary");
                req.Add(new StringContent(data.ToString(Formatting.None)), "data");

                var responseMsg = await httpClient.PostAsync(new Uri(Uri) + "GetStream", req).ConfigureAwait(false);

                var stream = await responseMsg.Content.ReadAsStreamAsync().ConfigureAwait(false);
                return Tuple.Create(stream, responseMsg.Content.Headers.ContentDisposition.FileName ?? responseMsg.Content.Headers.ContentDisposition.FileNameStar);
            }
            catch (Exception e)
            {
                OnException?.Invoke(e);

                throw;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<PersistentObject> ExecuteActionAsync(string action, PersistentObject parent = null, Query query = null, QueryResultItem[] selectedItems = null, Dictionary<string, string> parameters = null, bool skipHooks = false)
        {
            if (string.IsNullOrEmpty(action))
                throw new ArgumentException("message", nameof(action));

            var isObjectAction = action.StartsWith("PersistentObject.") || query == null;
            if (isObjectAction && parent == null)
                throw new ArgumentNullException(nameof(parent));

            try
            {
                IsBusy = true;

                if (!skipHooks)
                {
                    string fullTypeName;
                    if (!isObjectAction)
                    {
                        query.SetNotification(null);
                        fullTypeName = query.PersistentObject.FullTypeName;
                    }
                    else
                    {
                        parent.SetNotification(null);
                        fullTypeName = parent.FullTypeName;
                    }

                    var args = new ExecuteActionArgs(this, action) { Parameters = parameters, PersistentObject = parent, Query = query, SelectedItems = selectedItems };
                    await Hooks.OnAction(args).ConfigureAwait(false);

                    if (args.IsHandled)
                        return args.Result;

                    args.IsHandled = ClientActions.Get(fullTypeName).OnAction(args);
                    if (args.IsHandled)
                        return args.Result;
                }

                var data = CreateData();
                data["action"] = action;
                data["query"] = query?.ToServiceObject();
                data["parent"] = parent?.ToServiceObject();
                data["selectedItems"] = selectedItems != null ? JArray.FromObject(selectedItems.Select(i => i?.ToServiceObject())) : null;
                var jParameters = parameters != null ? JObject.FromObject(parameters) : null;
                data["parameters"] = jParameters;

                JObject response;
                while (true)
                {
                    response = await PostAsync("ExecuteAction", data).ConfigureAwait(false);

                    // TODO: response["operations"]

                    var retry = (JObject)response["retry"];
                    if (retry == null)
                        break;

                    // Retry action, use hooks to repost
                    var jRetryPo = (JObject)retry["persistentObject"];
                    var retryPo = jRetryPo != null ? Hooks.OnConstruct(this, jRetryPo) : null;

                    var option = await Hooks.OnRetryAction((string)retry["title"], (string)retry["message"], ((JArray)retry["options"]).ToObject<string[]>(), retryPo).ConfigureAwait(false);
                    if (jParameters == null)
                        data["parameters"] = jParameters = new JObject();
                    jParameters["RetryActionOption"] = option;

                    if (retryPo != null)
                        data["retryPersistentObject"] = retryPo.ToServiceObject();
                }

                var ex = (string)response["exception"] ?? (string)response["ExceptionMessage"];
                if (!string.IsNullOrEmpty(ex))
                {
                    if (isObjectAction)
                        parent.SetNotification(ex);
                    else
                        query.SetNotification(ex);

                    return null;
                }

                AuthToken = (string)response["authToken"];
                await UpdateSession(response).ConfigureAwait(false);

                var jPo = (JObject)response["result"];
                return jPo != null ? Hooks.OnConstruct(this, jPo) : null;
            }
            catch (Exception e)
            {
                OnException?.Invoke(e);

                if (isObjectAction)
                    parent.SetNotification(e.Message);
                else
                    query.SetNotification(e.Message);

                return null;
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Private Methods

        private async Task UpdateSession(JObject response)
        {
            if (response["session"] != null)
            {
                var sessionPo = Hooks.OnConstruct(this, (JObject)response["session"]);
                if (sessionPo.FullTypeName == "Vidyano.Error" || !string.IsNullOrEmpty(sessionPo.Notification))
                    throw new Exception(sessionPo.Notification);

                if (Session != null)
                    await Session.RefreshFromResult(sessionPo).ConfigureAwait(false);
                else
                    Session = sessionPo;

                Hooks.OnSessionUpdated(Session);
            }
            else
                Session = null;
        }

        #endregion

        #region Public Methods

        public async Task SignOut()
        {
            Application = null;
            User = string.Empty;
            AuthToken = null;
            AuthorizationHeader = null;
            IsConnected = false;

            await Hooks.SignOut().ConfigureAwait(false);
        }

        public static NoInternetMessage GetNoInternetMessage(string language = null)
        {
            return noInternetMessages.TryGetValue(language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, out var result) ? result : noInternetMessages["en"];
        }

        #endregion

        #region From/To ServiceString

        private static object GetDefaultValue(Type type, string dataType = null)
        {
            if (dataType != null)
            {
                if (dataType == DataTypes.Date)
                    return DateTime.Today;
                if (dataType == DataTypes.DateTime)
                    return DateTime.Now;
                if (dataType == DataTypes.DateTimeOffset)
                    return DateTimeOffset.Now;
            }

            return defaultValues.GetOrAdd(type, t => t.GetTypeInfo().IsValueType ? Activator.CreateInstance(t) : null);
        }

        public static Type GetClrType(string type)
        {
            if (string.IsNullOrEmpty(type))
                return typeof(string);

            switch (type.ToUpperInvariant())
            {
                case "INT32":
                    return typeof(int);

                case "NULLABLEINT32":
                    return typeof(int?);

                case "UINT32":
                    return typeof(uint);

                case "NULLABLEUINT32":
                    return typeof(uint?);

                case "INT16":
                    return typeof(short);

                case "NULLABLEINT16":
                    return typeof(short?);

                case "UINT16":
                    return typeof(ushort);

                case "NULLABLEUINT16":
                    return typeof(ushort?);

                case "INT64":
                    return typeof(long);

                case "NULLABLEINT64":
                    return typeof(long?);

                case "UINT64":
                    return typeof(ulong);

                case "NULLABLEUINT64":
                    return typeof(ulong?);

                case "DECIMAL":
                    return typeof(decimal);

                case "NULLABLEDECIMAL":
                    return typeof(decimal?);

                case "DOUBLE":
                    return typeof(double);

                case "NULLABLEDOUBLE":
                    return typeof(double?);

                case "SINGLE":
                    return typeof(float);

                case "NULLABLESINGLE":
                    return typeof(float?);

                case "BYTE":
                    return typeof(byte);

                case "NULLABLEBYTE":
                    return typeof(byte?);

                case "SBYTE":
                    return typeof(sbyte);

                case "NULLABLESBYTE":
                    return typeof(sbyte?);

                case "TIME":
                    return typeof(TimeSpan);

                case "NULLABLETIME":
                    return typeof(TimeSpan?);

                case "DATETIME":
                case "DATE":
                    return typeof(DateTime);

                case "NULLABLEDATETIME":
                case "NULLABLEDATE":
                    return typeof(DateTime?);

                case "DATETIMEOFFSET":
                    return typeof(DateTimeOffset);

                case "NULLABLEDATETIMEOFFSET":
                    return typeof(DateTimeOffset?);

                case "BOOLEAN":
                case "YESNO":
                    return typeof(bool);

                case "NULLABLEBOOLEAN":
                    return typeof(bool?);

                case "ENUM":
                case "FLAGSENUM":
                    return typeof(Enum); // NOTE: Can't know correct Enum type

                case "IMAGE":
                    return typeof(byte[]);

                case "GUID":
                    return typeof(Guid);

                case "NULLABLEGUID":
                    return typeof(Guid?);

                default:
                    return typeof(string);
            }
        }

        public static object FromServiceString(string value, string typeName)
        {
            var type = GetClrType(typeName);

            try
            {
                if (type == typeof(string))
                    return value;

                if (value == null)
                    return GetDefaultValue(type, typeName);

                if (value == string.Empty && Nullable.GetUnderlyingType(type) != null)
                    return GetDefaultValue(type, typeName);

                if (type == typeof(Guid) || type == typeof(Guid?))
                    return Guid.Parse(value);

                if (type == typeof(Enum) || type.GetTypeInfo().IsEnum)
                    return value;

                if (typeName == "Image")
                    return Current.Hooks.ByteArrayToImageSource(new MemoryStream(Convert.FromBase64String(value), true));

                if (type == typeof(byte[]))
                    return Convert.FromBase64String(value);

                if (defaultConverterTypes.Contains(type))
                {
                    var underlyingType = Nullable.GetUnderlyingType(type);
                    if (underlyingType != null)
                        type = underlyingType;

                    return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
                }

                if (type == typeof(DateTime) || type == typeof(DateTime?))
                    return DateTime.ParseExact(value, "dd-MM-yyyy HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);

                if (type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?))
                    return DateTimeOffset.ParseExact(value, "dd-MM-yyyy HH:mm:ss.FFFFFFF K", CultureInfo.InvariantCulture);

                if (type == typeof(TimeSpan) || type == typeof(TimeSpan?))
                    return TimeSpan.ParseExact(value, "G", CultureInfo.InvariantCulture);

                return null;
            }
            catch
            {
                return GetDefaultValue(type, typeName);
            }
        }

        public static string ToServiceString(object value)
        {
            if (value == null)
                return null;

            var type = value.GetType();

            if (type == typeof(string))
                return (string)value;

            if (type == typeof(byte[]))
                return Convert.ToBase64String((byte[])value);

            if (defaultConverterTypes.Contains(type))
                return Convert.ToString(value, CultureInfo.InvariantCulture);

            if (type == typeof(DateTime) || type == typeof(DateTime?))
                return ((DateTime)value).ToString("dd-MM-yyyy HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);

            if (type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?))
                return ((DateTimeOffset)value).ToString("dd-MM-yyyy HH:mm:ss.FFFFFFF K", CultureInfo.InvariantCulture);

            if (type == typeof(TimeSpan) || type == typeof(TimeSpan?))
                return ((TimeSpan)value).ToString("G", CultureInfo.InvariantCulture);

            return value.ToString();
        }

        #endregion

        #region Nested Types

        public sealed class NoInternetMessage
        {
            public NoInternetMessage(string title, string message, string tryAgain)
            {
                Title = title;
                Message = message;
                TryAgain = tryAgain;
            }

            public string Title { get; private set; }
            public string Message { get; private set; }
            public string TryAgain { get; private set; }
        }

        #endregion
    }
}