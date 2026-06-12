using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
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
                typeof(byte?), typeof(sbyte?), typeof(char?), typeof(short?), typeof(ushort?), typeof(int?), typeof(uint?), typeof(long?), typeof(ulong?), typeof(float?), typeof(double?), typeof(decimal?), typeof(bool?), typeof(Guid?), typeof(byte[]),
            });

        private static readonly Dictionary<Type, object> defaultValues = new Dictionary<Type, object>();

        private static readonly UTF8Encoding utf8NoBom = new UTF8Encoding(false);

        private static readonly Dictionary<string, Type> clrTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["INT32"] = typeof(int),
            ["NULLABLEINT32"] = typeof(int?),
            ["UINT32"] = typeof(uint),
            ["NULLABLEUINT32"] = typeof(uint?),
            ["INT16"] = typeof(short),
            ["NULLABLEINT16"] = typeof(short?),
            ["UINT16"] = typeof(ushort),
            ["NULLABLEUINT16"] = typeof(ushort?),
            ["INT64"] = typeof(long),
            ["NULLABLEINT64"] = typeof(long?),
            ["UINT64"] = typeof(ulong),
            ["NULLABLEUINT64"] = typeof(ulong?),
            ["DECIMAL"] = typeof(decimal),
            ["NULLABLEDECIMAL"] = typeof(decimal?),
            ["DOUBLE"] = typeof(double),
            ["NULLABLEDOUBLE"] = typeof(double?),
            ["SINGLE"] = typeof(float),
            ["NULLABLESINGLE"] = typeof(float?),
            ["BYTE"] = typeof(byte),
            ["NULLABLEBYTE"] = typeof(byte?),
            ["SBYTE"] = typeof(sbyte),
            ["NULLABLESBYTE"] = typeof(sbyte?),
            ["TIME"] = typeof(TimeSpan),
            ["NULLABLETIME"] = typeof(TimeSpan?),
            ["DATETIME"] = typeof(DateTime),
            ["DATE"] = typeof(DateTime),
            ["NULLABLEDATETIME"] = typeof(DateTime?),
            ["NULLABLEDATE"] = typeof(DateTime?),
            ["DATETIMEOFFSET"] = typeof(DateTimeOffset),
            ["NULLABLEDATETIMEOFFSET"] = typeof(DateTimeOffset?),
            ["BOOLEAN"] = typeof(bool),
            ["YESNO"] = typeof(bool),
            ["NULLABLEBOOLEAN"] = typeof(bool?),
            ["ENUM"] = typeof(Enum), // NOTE: Can't know correct Enum type
            ["FLAGSENUM"] = typeof(Enum),
            ["IMAGE"] = typeof(byte[]),
            ["GUID"] = typeof(Guid),
            ["NULLABLEGUID"] = typeof(Guid?),
        };

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

        private PersistentObject _Application, _Session, _Initial;
        private bool _IsBusy, _IsConnected, _IsUsingDefaultCredentials;
        private KeyValueList<string, string> _Messages;
        // (source Uri, normalized base) — one immutable pair so the cache swap is a single
        // atomic reference write; paired fields could be observed torn on weak memory models.
        private Tuple<string, string> serviceUriCache;

        #endregion

        #region Constructors

        public Client(HttpClient client = null)
        {
            Current = this;

            if (client is null)
            {
                // Create HttpClient with cookie support
                var handler = new HttpClientHandler
                {
                    CookieContainer = new(),
                    UseCookies = true,
#if !NETSTANDARD2_0
                    AutomaticDecompression = DecompressionMethods.All,
#else
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
#endif
                };

                httpClient = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(90),
                };
            }
            else
                httpClient = client;
        }

        #endregion

        #region Properties

        public event Action<Exception> OnException;

        public static Client Current { get; private set; }

        public static bool StrictMode { get; set; }

        public PersistentObject Application
        {
            get => _Application;
            set
            {
                if (SetProperty(ref _Application, value))
                    _programUnits = null;
            }
        }

        private IReadOnlyList<ProgramUnit> _programUnits;

        /// <summary>
        /// The typed program-unit tree parsed from
        /// <c>Application.GetAttribute("ProgramUnits").ValueDirect</c>. Returns an empty list when no
        /// Application has been loaded or the signed-in user has no program-unit rights. Recomputed
        /// once when <see cref="Application"/> is assigned, then cached.
        /// </summary>
        public IReadOnlyList<ProgramUnit> ProgramUnits
        {
            get
            {
                // Capture into a local so a concurrent Application setter (which nulls
                // _programUnits) can't turn this into a null return between the check and the use.
                var cached = _programUnits;
                if (cached != null)
                    return cached;

                var raw = Application?.GetAttribute("ProgramUnits")?.ValueDirect;
                if (string.IsNullOrEmpty(raw))
                    return _programUnits = Array.Empty<ProgramUnit>();

                try
                {
                    var parsed = JObject.Parse(raw);
                    if (!(parsed["units"] is JArray units))
                        return _programUnits = Array.Empty<ProgramUnit>();
                    return _programUnits = units.OfType<JObject>().Select(u => new ProgramUnit(u)).ToArray();
                }
                catch
                {
                    return _programUnits = Array.Empty<ProgramUnit>();
                }
            }
        }

        public PersistentObject Session
        {
            get => _Session;
            set => SetProperty(ref _Session, value);
        }

        /// <summary>
        /// The Initial <see cref="PersistentObject"/> returned by <c>GetApplication</c> when the
        /// server gates the application behind a one-shot PO — for example license-terms
        /// acceptance, forced two-factor enrolment, or a forced password reset. Populated during
        /// sign-in; <c>null</c> when the server emits no gate. Drive the PO to a successful
        /// <c>Save</c> (or otherwise resolve it) and then call <see cref="ClearInitial"/> to
        /// signal the rest of the client that the gate is done; the v4 frontend's sign-in
        /// component follows the same pattern. Never auto-roundtripped on subsequent requests.
        /// Callers that ignore this property bypass the gate entirely.
        /// </summary>
        public PersistentObject Initial
        {
            get => _Initial;
            private set => SetProperty(ref _Initial, value);
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

        // new Uri(...) preserves the pre-change normalization; the appended "/" makes a missing
        // trailing slash work instead of silently producing https://host/appMethod (404).
        private string GetServiceUri(string method)
        {
            var uri = Uri;
            var cache = serviceUriCache;
            if (cache == null || uri != cache.Item1)
            {
                var normalized = new Uri(uri).ToString();
                cache = Tuple.Create(uri, normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/");
                serviceUriCache = cache;
            }

            return cache.Item2 + method;
        }

        private static HttpContent CreateRequestContent(JObject data)
        {
            var stream = new MemoryStream();
            using (var writer = new JsonTextWriter(new StreamWriter(stream, utf8NoBom, 1024, leaveOpen: true)) { Formatting = Formatting.None })
                data.WriteTo(writer);

            stream.Position = 0;
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
            return content;
        }

        public async Task<ClientData> GetClientData(string environment = null)
        {
            try
            {
                IsBusy = true;

                return new ClientData(JObject.Parse(await httpClient.GetStringAsync(GetServiceUri("GetClientData?environment=" + System.Uri.EscapeDataString(environment ?? Hooks.Environment ?? string.Empty))).ConfigureAwait(false)));
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
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, GetServiceUri(method)) { Content = CreateRequestContent(data) };
                if (AuthorizationHeader != null)
                    requestMessage.Headers.Authorization = AuthorizationHeader;
                responseMsg = await httpClient.SendAsync(requestMessage).ConfigureAwait(false);
            }
#if !NETSTANDARD2_0
            catch (OperationCanceledException oce) when (oce.InnerException is TimeoutException)
            {
                return Log(new JObject(new JProperty("exception", "Timeout: request took longer than " + httpClient.Timeout)));
            }
            catch (OperationCanceledException)
            {
                return Log(new JObject(new JProperty("exception", "Cancelled: the request was cancelled")));
            }
#else
            catch (OperationCanceledException)
            {
                return Log(new JObject(new JProperty("exception", "Timeout: request took longer than " + httpClient.Timeout)));
            }
#endif
            catch (Exception responseEx)
            {
                return Log(new JObject(new JProperty("exception", GetNoInternetMessage().Message + "\n\nException: " + responseEx)));
            }

            JObject response;
            using (responseMsg)
            {
                if (!responseMsg.IsSuccessStatusCode)
                    return Log(new JObject(new JProperty("exception", "error, status: " + responseMsg.StatusCode)));

                using var stream = await responseMsg.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var streamReader = CreateResponseReader(stream, responseMsg.Content);
                using var jsonReader = new JsonTextReader(streamReader);

                // ContentLength is unreliable for chunked responses; an empty body is detected by the reader.
                if (!await jsonReader.ReadAsync().ConfigureAwait(false))
                    return Log(new JObject(new JProperty("exception", GetNoInternetMessage().Title)));

                response = await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
            }

            var ex = (string)response["exception"];
            if (!string.IsNullOrEmpty(ex) && ex == "Session expired")
            {
                if (IsUsingDefaultCredentials)
                {
                    data.Remove("password");
                    data.Remove("authToken");

                    using var retryResponseMsg = await httpClient.PostAsync(GetServiceUri(method), CreateRequestContent(data)).ConfigureAwait(false);
                    using var retryStream = await retryResponseMsg.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var retryStreamReader = CreateResponseReader(retryStream, retryResponseMsg.Content);
                    using var retryJsonReader = new JsonTextReader(retryStreamReader);
                    response = await JObject.LoadAsync(retryJsonReader).ConfigureAwait(false);
                }
                else
                {
                    IsConnected = false;
                    // TODO: Redirect to sign in
                }
            }

            DispatchClientOperations(response);

            return Log(response);
        }

        // ReadAsStringAsync honored the Content-Type charset; keep that for non-UTF-8 servers.
        // An unknown/invalid charset falls back to UTF-8 (with BOM detection) instead of throwing.
        private static StreamReader CreateResponseReader(Stream stream, HttpContent content)
        {
            var charSet = content.Headers.ContentType?.CharSet?.Trim('"');
            if (!string.IsNullOrEmpty(charSet))
            {
                try
                {
                    return new StreamReader(stream, Encoding.GetEncoding(charSet));
                }
                catch (ArgumentException)
                {
                }
            }

            return new StreamReader(stream);
        }

        private void DispatchClientOperations(JObject response)
        {
            if (!(response["operations"] is JArray ops))
                return;

            foreach (var raw in ops.OfType<JObject>())
                Hooks?.OnClientOperation(ClientOperation.FromJson(raw));
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

                // Populate Actions before constructing the Initial PO below: that PO can carry
                // actions, which GetActions resolves against this table during construction.
                Messages = new KeyValueList<string, string>(Application.Queries["ClientMessages"].ToDictionary(item => (string)item["Key"], item => (string)item["Value"]), true);
                Actions = Application.Queries["Actions"].ToDictionary(item => (string)item["Name"], item => new ActionBase.Definition
                {
                    Name = (string)item["Name"],
                    DisplayName = (string)item["DisplayName"],
                    IsPinned = (bool)item["IsPinned"],
                    RefreshQueryOnCompleted = (bool)item["RefreshQueryOnCompleted"],
                    Offset = (int)item["Offset"],
                    Options = ((string)item["Options"] ?? string.Empty).Trim().Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(str => str.Trim()).ToArray(),
                    SelectionRule = ExpressionParser.Get((string)item["SelectionRule"]),
                });

                if (response["initial"] is JObject initialJson)
                {
                    var initialPo = Hooks.OnConstruct(this, initialJson);
                    if (initialPo.FullTypeName == "Vidyano.Error" || (initialPo.HasNotification && initialPo.NotificationType == NotificationType.Error))
                        throw new Exception(initialPo.Notification);
                    Initial = initialPo;
                }
                else
                    Initial = null;

                var cultureInfo = new CultureInfo(Application["Culture"].ValueDirect);
                CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
                CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

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

        public async Task<Query> GetQueryAsync(string id, string filterName = null, ColumnOverride[] columnOverrides = null)
        {
            try
            {
                IsBusy = true;

                var data = CreateData();
                data["id"] = id;
                data["filterName"] = filterName;
                if (columnOverrides != null && columnOverrides.Length > 0)
                    data["columnOverrides"] = JArray.FromObject(columnOverrides);

                var response = await PostAsync("GetQuery", data).ConfigureAwait(false);

                var ex = (string)response["exception"] ?? (string)response["ExceptionMessage"];
                if (!string.IsNullOrEmpty(ex))
                    throw new Exception(ex);

                AuthToken = (string)response["authToken"];
                await UpdateSession(response).ConfigureAwait(false);

                var result = (JObject)response["query"];
                var query = result != null ? Hooks.OnConstruct(this, result, null, false) : null;
                if (query != null && columnOverrides != null && columnOverrides.Length > 0)
                    query.ColumnOverrides = columnOverrides;

                return query;
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

        public async Task<JObject> ExecuteQueryAsync(Query query, PersistentObject parent = null, string filterName = null, bool asLookup = false, ColumnOverride[] columnOverrides = null)
        {
            try
            {
                IsBusy = true;

                var data = CreateData();
                data["query"] = query.ToServiceObject();
                data["parent"] = parent?.ToServiceObject();
                data["filterName"] = filterName;
                data["asLookup"] = asLookup;
                if (columnOverrides != null && columnOverrides.Length > 0)
                    data["columnOverrides"] = JArray.FromObject(columnOverrides);

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

        /// <summary>
        ///     Downloads a registered stream from the server. The returned stream reads directly from
        ///     the live HTTP response: dispose it promptly when done — an undisposed stream keeps the
        ///     underlying connection open, and faults during reading surface at the reader.
        /// </summary>
        public async Task<Tuple<Stream, string>> GetStreamAsync(PersistentObject registeredStream) //, string action = null, PersistentObject parent = null, Query query = null, QueryResultItem[] selectedItems = null, Dictionary<string, string> parameters = null)
        {
            try
            {
                IsBusy = true;

                var data = CreateData();
                if (registeredStream != null)
                    data["id"] = registeredStream.ObjectId;

                var req = new MultipartFormDataContent("VidyanoBoundary")
                {
                    { CreateRequestContent(data), "data" },
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, GetServiceUri("GetStream")) { Content = req };
                var responseMsg = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!responseMsg.IsSuccessStatusCode)
                {
                    var error = $"GetStream failed: {(int)responseMsg.StatusCode} ({responseMsg.ReasonPhrase})";
                    responseMsg.Dispose();
                    throw new Exception(error);
                }

                // The returned stream reads directly from the live response (ResponseHeadersRead);
                // disposing responseMsg here would close it, so it is deliberately left undisposed.
                var stream = await responseMsg.Content.ReadAsStreamAsync().ConfigureAwait(false);
                return Tuple.Create(stream, responseMsg.Content.Headers.ContentDisposition?.FileName ?? responseMsg.Content.Headers.ContentDisposition?.FileNameStar);
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
                data["selectedItems"] = selectedItems != null ? new JArray(selectedItems.Select(i => i?.ToServiceObject())) : null;
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
                if (sessionPo.FullTypeName == "Vidyano.Error" || (sessionPo.HasNotification && sessionPo.NotificationType == NotificationType.Error))
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

        /// <summary>
        /// Sets <see cref="Initial"/> to <c>null</c>. Mirrors the v4 frontend's
        /// <c>service.clearInitial()</c>: after driving the gate PO to a successful <c>Save</c> (or
        /// otherwise resolving it), call this so the rest of the client sees a gate-free state.
        /// No-op when <see cref="Initial"/> is already <c>null</c>.
        /// </summary>
        public void ClearInitial()
        {
            Initial = null;
        }

        public async Task SignOut()
        {
            // Try to call viSignOut action on the Application before clearing auth tokens
            if (Application != null)
            {
                try
                {
                    await ExecuteActionAsync("PersistentObject.viSignOut", Application, skipHooks: true).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore any errors during sign out action execution
                    // Server might be unreachable or action might not exist
                }
            }

            // Clear local state
            Application = null;
            Initial = null;
            User = string.Empty;
            AuthToken = null;
            AuthorizationHeader = null;
            IsConnected = false;

            // Call hooks for additional cleanup
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

            return clrTypes.TryGetValue(type, out var clrType) ? clrType : typeof(string);
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

            public string Title { get; }
            public string Message { get; }
            public string TryAgain { get; private set; }
        }

        #endregion
    }
}