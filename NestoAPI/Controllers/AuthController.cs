// Usings necesarios
using NestoAPI.Infraestructure.Clientes;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using NestoAPI.Providers;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Runtime.Caching;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;


public class AuthController : ApiController
{
    private readonly string SecretKey = ConfigurationManager.AppSettings["as:AudienceSecret"];

    private readonly IGestorClientes _gestorClientes;
    private readonly IServicioCorreoElectronico _servicioCorreo;
    private const int ExpirationMinutes = 10;
    private static readonly MemoryCache cache = MemoryCache.Default;
    private ApplicationUserManager _userManager = null;

    protected ApplicationUserManager UserManager
    {
        get
        {
            return _userManager ?? Request.GetOwinContext().GetUserManager<ApplicationUserManager>();
        }
    }

    public AuthController(IGestorClientes gestorClientes, IServicioCorreoElectronico servicioCorreo)
    {
        _gestorClientes = gestorClientes;
        _servicioCorreo = servicioCorreo;
    }

    [HttpPost]
    [Route("api/auth/request-code")]
    public async Task<IHttpActionResult> RequestCodeAsync(ClientValidationRequest request)
    {
        string email = request.Email;
        string nif = request.NIF;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(nif))
        {
            return BadRequest("Email y NIF requeridos.");
        }

        // Validar si existe el cliente en la BBDD
        string cliente = await BuscarCliente(email, nif);
        if (string.IsNullOrEmpty(cliente))
        {
            return Unauthorized();
        }

        // Generar código
        string codigo = GenerarCodigo();

        // Generar token de validación
        byte[] tokenBytes = new byte[32];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(tokenBytes);
        }
        string tokenForValidation = Convert.ToBase64String(tokenBytes);

        // Guardar en caché
        var datos = new CodigoValidacionTemporal
        {
            Codigo = codigo,
            Expira = DateTime.UtcNow.AddMinutes(10),
            Email = email,
            NIF = nif
        };

        cache.Set(tokenForValidation, datos, DateTimeOffset.UtcNow.AddMinutes(10));


        string mensajeHtml = $@"
                    <html>
                        <body>
                            <h2>Tu código de verificación</h2>
                            <p>Usa el siguiente código para acceder a tu cuenta:</p>
                            <h1>{codigo}</h1>
                            <p>Si no solicitaste este código, ignora este mensaje.</p>
                        </body>
                    </html>
                ";

        // Enviar correo
        MailMessage mail = new MailMessage(Constantes.Correos.TIENDA_ONLINE, email)
        {
            Subject = "Tu código de validación",
            Body = mensajeHtml,
            IsBodyHtml = true
        };

        bool enviado = _servicioCorreo.EnviarCorreoSMTP(mail);
        if (!enviado)
        {
            return InternalServerError(new Exception("No se pudo enviar el correo."));
        }

        // Devolver solo el token
        return Ok(new
        {
            tokenForValidation
        });
    }

    [HttpPost]
    [Route("api/auth/validate-code")]
    public async Task<IHttpActionResult> ValidateCodeAsync([FromBody] CodigoValidacionModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Email) ||
            string.IsNullOrWhiteSpace(model.Token) ||
            string.IsNullOrWhiteSpace(model.Codigo))
        {
            return BadRequest("Faltan datos obligatorios.");
        }

        // Buscamos por clave el token recibido
        if (!(cache.Get(model.Token) is CodigoValidacionTemporal entry))
        {
            return Unauthorized(); // Token no encontrado o expirado
        }

        // Verificamos que coincidan email y código.
        // NestoAPI#428 (punto 5): el codigo se compara en TIEMPO CONSTANTE. Con Equals, el tiempo
        // de respuesta delata cuantos digitos se han acertado, y adivinarlo pasa de 900.000
        // intentos a unas decenas. El email no es un secreto y se compara normal.
        if (!entry.Email.Equals(model.Email, StringComparison.OrdinalIgnoreCase) ||
            !ComparacionSegura.SonIguales(entry.Codigo, model.Codigo))
        {
            return Unauthorized(); // Datos incorrectos
        }

        // Opcional: eliminar el código para que solo se use una vez
        _ = cache.Remove(model.Token);

        // Recuperamos el NIF del entry y volvemos a buscar el cliente para obtener el valor correcto
        string nif = entry.NIF;
        string cliente = await BuscarCliente(model.Email, nif);
        if (string.IsNullOrEmpty(cliente))
        {
            return Unauthorized();
        }

        // Crear claims del usuario
        string tokenJwt = await CrearJWTAsync(model.Email, nif, cliente);

        return Ok(new
        {
            token = tokenJwt
        });
    }


    [HttpPost]
    [Route("api/auth/token")]
    public async Task<IHttpActionResult> GetToken(CodigoValidacionRequest request)
    {
        string expectedPayloadPrefix = $"{request.Code}:{request.Email}:{request.NIF}:";

        for (int i = 0; i < ExpirationMinutes; i++)
        {
            long ticks = DateTime.UtcNow.AddMinutes(-i).Ticks;
            string fullPayload = $"{expectedPayloadPrefix}{ticks}";
            string expectedSignature = FirmarConHMAC(fullPayload, SecretKey);

            // NestoAPI#428 (punto 5): en tiempo constante, que esto es una firma HMAC.
            if (ComparacionSegura.SonIguales(expectedSignature, request.TokenForValidation))
            {
                // Recuperamos el cliente para el JWT
                string cliente = await BuscarCliente(request.Email, request.NIF);
                if (string.IsNullOrEmpty(cliente))
                {
                    return Unauthorized();
                }
                string token = await CrearJWTAsync(request.Email, request.NIF, cliente);
                return Ok(new { token });
            }
        }

        return Unauthorized();
    }


    [HttpPost]
    [Route("api/auth/refreshToken")]
    public async Task<IHttpActionResult> RefreshToken()
    {
        if (Request.Headers.Authorization == null || string.IsNullOrEmpty(Request.Headers.Authorization.Parameter))
        {
            return Unauthorized();
        }

        string accessToken = Request.Headers.Authorization.Parameter;

        try
        {
            // NestoAPI#427: antes esto era ReadJwtToken, que SOLO parsea. Cualquiera podia
            // fabricarse un JWT con el cliente que quisiera y canjearlo aqui por uno autentico.
            // ValidarFirmaSinCaducidad comprueba firma, issuer y audiencia — pero NO la caducidad,
            // porque la app manda tokens caducados a proposito (ver el comentario del validador).
            JwtSecurityToken token = ValidadorJwt.ValidarFirmaSinCaducidad(accessToken);
            if (token == null)
            {
                return Unauthorized();
            }

            // Validar expiración manualmente (#430: la ventana NO se toca en esta tanda)
            if (token.ValidTo < DateTime.UtcNow.AddMonths(-1))
            {
                return Unauthorized();
            }

            string email = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            string nif = token.Claims.FirstOrDefault(c => c.Type == "nif")?.Value;
            string cliente = token.Claims.FirstOrDefault(c => c.Type == "cliente")?.Value;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(nif) || string.IsNullOrEmpty(cliente))
            {
                return Unauthorized();
            }

            string newToken = await CrearJWTAsync(email, nif, cliente);
            return Ok(new { token = newToken });
        }
        catch
        {
            return Unauthorized();
        }
    }

    /// <summary>
    /// Refresca un token OAuth expirado (usado por NestoApp).
    /// Los tokens OAuth son generados por /oauth/token y tienen claims de Identity + vendedor.
    /// Se distinguen de tokens de clientes porque NO tienen claim "cliente".
    /// </summary>
    [HttpPost]
    [Route("api/auth/refreshOAuthToken")]
    public async Task<IHttpActionResult> RefreshOAuthToken()
    {
        if (Request.Headers.Authorization == null || string.IsNullOrEmpty(Request.Headers.Authorization.Parameter))
        {
            return Unauthorized();
        }

        string accessToken = Request.Headers.Authorization.Parameter;

        try
        {
            // NestoAPI#427: mismo agujero que en RefreshToken, y aqui el claim que se creia a
            // ciegas era el userName con el que se busca el usuario en Identity.
            JwtSecurityToken token = ValidadorJwt.ValidarFirmaSinCaducidad(accessToken);
            if (token == null)
            {
                return Unauthorized();
            }

            // Validar que no esté expirado hace más de 2 años (temporal, ir bajando hasta 1 mes)
            // (#430: la ventana NO se toca en esta tanda; va con #427 punto 4)
            if (token.ValidTo < DateTime.UtcNow.AddYears(-2))
            {
                return Unauthorized();
            }

            // Detectar si es un token OAuth: NO tiene claim "cliente"
            string cliente = token.Claims.FirstOrDefault(c => c.Type == "cliente")?.Value;
            if (!string.IsNullOrEmpty(cliente))
            {
                // Es un token de cliente, no OAuth - redirigir al endpoint correcto
                return BadRequest("Este endpoint es solo para tokens OAuth. Use api/auth/refreshToken para tokens de clientes.");
            }

            // Extraer username del token (puede estar en Name o NameIdentifier)
            string userName = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value
                ?? token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
                ?? token.Claims.FirstOrDefault(c => c.Type == "unique_name")?.Value;

            if (string.IsNullOrEmpty(userName))
            {
                return Unauthorized();
            }

            // Buscar usuario en Identity
            ApplicationUser user = await UserManager.FindByNameAsync(userName);
            if (user == null)
            {
                return Unauthorized();
            }

            // Regenerar el token con la misma lógica de CustomOAuthProvider
            string newToken = await CrearJWTParaOAuthAsync(user);
            return Ok(new { token = newToken });
        }
        catch
        {
            return Unauthorized();
        }
    }

    [HttpPost]
    [Route("api/auth/windows-token")]
    public async Task<IHttpActionResult> GetWindowsToken()
    {
        // Debug para ver qué está pasando
        System.Diagnostics.Debug.WriteLine($"AuthType: {User.Identity.AuthenticationType}");
        System.Diagnostics.Debug.WriteLine($"IsAuthenticated: {User.Identity.IsAuthenticated}");
        System.Diagnostics.Debug.WriteLine($"Name: {User.Identity.Name}");

        // Si no está autenticado, hacer challenge
        if (!User.Identity.IsAuthenticated ||
            (User.Identity.AuthenticationType != "NTLM" && User.Identity.AuthenticationType != "Negotiate"))
        {
            var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            response.Headers.Add("WWW-Authenticate", "Negotiate");
            response.Headers.Add("WWW-Authenticate", "NTLM");
            return ResponseMessage(response);
        }

        if (!(User.Identity is WindowsIdentity windowsIdentity))
        {
            return Unauthorized();
        }

        // Opcional: Verificar que pertenezca a un grupo específico de empleados
        var principal = new WindowsPrincipal(windowsIdentity);
        if (!principal.IsInRole("NUEVAVISION\\Usuarios del dominio"))
        {
            // Listar todos los grupos para debug
            foreach (var group in windowsIdentity.Groups)
            {
                try
                {
                    var sid = group.Translate(typeof(NTAccount));
                    System.Diagnostics.Debug.WriteLine($"Grupo: {sid}");
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine($"Grupo SID: {group}");
                }
            }
            return Unauthorized();
        }

        // Crear JWT para empleado
        string token = await CrearJWTParaEmpleadoAsync(windowsIdentity.Name);
        return Ok(new { token });
    }

    // NestoAPI#429 (punto 1): la validacion de la API key sale del cuerpo del metodo y pasa al
    // atributo, que FALLA EN CERRADO. La comprobacion de antes era `apiKey != _apiKeyPrestashop`
    // con el campo leido de configuracion: si la setting no estaba definida valia null, una
    // peticion sin cabecera dejaba apiKey a null, y null != null es falso — la validacion se
    // superaba y este endpoint, que emite JWT de cliente SALTANDOSE el codigo por correo, quedaba
    // abierto a internet. Como secretos.config no esta en control de versiones, bastaba un
    // despliegue con ese fichero mal copiado.
    /// <summary>
    /// SSO servidor-a-servidor para el módulo de vídeos de PrestaShop (commit b40d1f5, 13/10/25):
    /// emite un JWT de cliente SALTÁNDOSE el código por correo, porque quien llama es PrestaShop,
    /// que ya autenticó al cliente por su cuenta, y se identifica con la API key.
    ///
    /// <para><b>NO es un mecanismo de login de cara al usuario</b>, aunque el nombre lo sugiera:
    /// un login de usuario nuevo (el módulo de login por CIF, por ejemplo) va por
    /// request-code + validate-code, como TiendasNuevaVision. Escrito aquí porque el rodeo ya
    /// se dio una vez (NestoAPI#426).</para>
    ///
    /// <para>NestoAPI#426: devuelve, además del token, lo que el consumidor necesita sin tener
    /// que decodificar el JWT ni inventarse nada: la caducidad REAL (el módulo la hardcodeaba a
    /// 1 hora), el nombre del cliente y las compras recientes como bool. Los tres se leen del
    /// token/ficha recién emitidos, así que si algún día cambia ExpiresUtc en CrearJWTAsync,
    /// esto sigue diciendo la verdad.</para>
    /// </summary>
    [AllowAnonymous]
    [ApiKey("ApiKeyPrestashop", "X-API-KEY")]
    [HttpPost]
    [Route("api/auth/prestashop-login")]
    public async Task<IHttpActionResult> PrestashopLogin([FromBody] PrestashopLoginRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("Debe especificar el email.");
        }

        // Buscar cliente en base de datos (la ficha entera: el nombre viaja en la respuesta)
        ClienteDTO cliente = await _gestorClientes.BuscarClientePorEmailNif(request.Email, request.Nif);
        if (cliente is null || string.IsNullOrEmpty(cliente.cliente))
        {
            return Unauthorized();
        }

        // Generar el mismo token JWT que en los otros flujos
        string token = await CrearJWTAsync(request.Email, request.Nif, cliente.cliente);

        JwtSecurityToken emitido = new JwtSecurityTokenHandler().ReadJwtToken(token);
        return Ok(new
        {
            token,
            expiration = emitido.ValidTo,
            nombre = cliente.nombre,
            hasRecentPurchases = emitido.Claims.Any(c => c.Type == "HasRecentPurchases" && c.Value == "true")
        });
    }


    private async Task<string> BuscarCliente(string email, string nif)
    {
        ClienteDTO cliente = await _gestorClientes.BuscarClientePorEmailNif(email, nif);
        return !(cliente is null) && !string.IsNullOrEmpty(cliente.cliente) ? cliente.cliente : string.Empty;
    }

    /// <summary>
    /// NestoAPI#428 (punto 2): el codigo de 6 digitos que se manda por correo.
    ///
    /// Antes era `new Random().Next(100000, 999999)`. Un `new Random()` sin semilla en .NET
    /// Framework se siembra del reloj del sistema, con resolucion de unos 15 ms: dos codigos
    /// pedidos dentro del mismo tick salian IDENTICOS. Unas lineas mas arriba, en el mismo
    /// metodo, ya se usaba RandomNumberGenerator para el tokenForValidation.
    ///
    /// El bucle es RECHAZO POR SESGO, no un capricho: 2^32 no es multiplo de los 900.000 valores
    /// posibles, asi que un modulo a secas haria unos codigos mas probables que otros y le
    /// regalaria trabajo a quien los adivine. Se descartan los valores del ultimo tramo
    /// incompleto y se vuelve a tirar. Da una vuelta de mas muy de vez en cuando.
    /// </summary>
    private string GenerarCodigo()
    {
        const int minimo = 100000;
        const int total = 900000;   // de 100000 a 999999, ambos incluidos

        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            byte[] bytes = new byte[4];
            uint limite = uint.MaxValue - (uint.MaxValue % total);

            while (true)
            {
                rng.GetBytes(bytes);
                uint valor = BitConverter.ToUInt32(bytes, 0);
                if (valor < limite)
                {
                    return (minimo + (int)(valor % total)).ToString();
                }
            }
        }
    }

    private string FirmarConHMAC(string texto, string clave)
    {
        byte[] key = Encoding.UTF8.GetBytes(clave);
        using (HMACSHA256 hmac = new HMACSHA256(key))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(texto);
            byte[] hash = hmac.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }

    /// <summary>
    /// Crea un JWT que incluye las claims: Email, NIF, Cliente y, si corresponde, HasRecentPurchases.
    /// </summary>
    /// <param name="email">Email del usuario</param>
    /// <param name="nif">NIF del usuario</param>
    /// <param name="cliente">Identificador o nombre del cliente</param>
    /// <returns>JWT string</returns>
    private async Task<string> CrearJWTAsync(string email, string nif, string cliente)
    {
        // Construir claims
        List<Claim> claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, email),
        new Claim(ClaimTypes.Email, email),
        new Claim(ClaimTypes.Name, email),
        new Claim("nif", nif),
        new Claim("cliente", cliente)
    };

        // Verificar si tiene compras recientes y añadir el claim correspondiente
        bool tieneComprasRecientes = await ClienteHelper.ClienteConComprasRecientesAsync(cliente);
        if (tieneComprasRecientes)
        {
            claims.Add(new Claim("HasRecentPurchases", "true"));
        }

        // NestoAPI#446: la persona de contacto con cargo "Pedidos sin ver precios" (30) o "sin
        // ver descuentos" (31) lleva su nivel en el JWT; con él el servidor tapa lo que toque y
        // fuerza la forma de pago habitual. Con varios cargos para el mismo correo manda el más
        // restrictivo. Se recalcula en cada refresco: cambiar el cargo en Nesto surte efecto en una hora.
        PoliticaPreciosOcultos.NivelPrecios nivelPrecios = await ClienteHelper.NivelPreciosAsync(cliente, email);
        if (nivelPrecios != PoliticaPreciosOcultos.NivelPrecios.Completo)
        {
            claims.Add(new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, nivelPrecios.ToString()));
        }

        // Crear la identidad especificando el AuthenticationType "JWT"
        ClaimsIdentity identity = new ClaimsIdentity(claims, "JWT");

        // Establecer propiedades de autenticación
        AuthenticationProperties props = new AuthenticationProperties
        {
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddHours(1)
        };

        // Crear el ticket de autenticación
        AuthenticationTicket ticket = new AuthenticationTicket(identity, props);

        // Utilizar el CustomJwtFormat para generar el token (se encarga de formatear el JWT)
        CustomJwtFormat jwtFormat = new CustomJwtFormat(ConfigurationManager.AppSettings["JwtIssuer"]);
        return jwtFormat.Protect(ticket);
    }

    /// <summary>
    /// Crea un JWT específico para empleados autenticados por Windows
    /// </summary>
    /// <param name="windowsUserName">Nombre del usuario de Windows (ej: DOMINIO\usuario)</param>
    /// <returns>JWT string</returns>
    private async Task<string> CrearJWTParaEmpleadoAsync(string windowsUserName)
    {
        try
        {
            var windowsIdentity = HttpContext.Current.User.Identity as WindowsIdentity;
            _ = new WindowsPrincipal(windowsIdentity);

            // Construir claims para empleado
            List<Claim> claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, windowsUserName),
            new Claim(ClaimTypes.Name, windowsUserName),
            new Claim(ClaimTypes.AuthenticationMethod, "Windows"),
            new Claim("IsEmployee", "true"), // Claim específico para identificar empleados
            new Claim("HasRecentPurchases", "true") // Los empleados pueden ver todo
        };

            foreach (var group in windowsIdentity.Groups)
            {
                try
                {
                    var sid = group.Translate(typeof(NTAccount));
                    claims.Add(new Claim(ClaimTypes.Role, sid.ToString()));
                }
                catch { }
            }

            // Crear la identidad especificando el AuthenticationType "JWT"
            ClaimsIdentity identity = new ClaimsIdentity(claims, "JWT");

            // Establecer propiedades de autenticación (token más largo para empleados)
            AuthenticationProperties props = new AuthenticationProperties
            {
                IssuedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddHours(8) // 8 horas para empleados
            };

            // Crear el ticket de autenticación
            AuthenticationTicket ticket = new AuthenticationTicket(identity, props);

            // Utilizar el CustomJwtFormat existente para generar el token
            CustomJwtFormat jwtFormat = new CustomJwtFormat(ConfigurationManager.AppSettings["JwtIssuer"]);
            return jwtFormat.Protect(ticket);
        }
        catch (Exception)
        {
            throw;
        }
    }

    /// <summary>
    /// Crea un JWT para usuarios OAuth (NestoApp).
    /// Replica la lógica de CustomOAuthProvider.GrantResourceOwnerCredentials.
    /// </summary>
    /// <param name="user">Usuario de Identity</param>
    /// <returns>JWT string</returns>
    private async Task<string> CrearJWTParaOAuthAsync(ApplicationUser user)
    {
        // Generar la identidad igual que en CustomOAuthProvider
        ClaimsIdentity oAuthIdentity = await user.GenerateUserIdentityAsync(UserManager, "JWT");

        // Añadir claims de vendedor si el usuario tiene uno asociado (Issue #70)
        try
        {
            ClaimsVendedorHelper.AñadirClaimsVendedor(oAuthIdentity, user.UserName);
        }
        catch (Exception)
        {
            // Si falla la búsqueda del vendedor, continuamos sin el claim
        }

        // Establecer propiedades de autenticación
        AuthenticationProperties props = new AuthenticationProperties
        {
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = DateTime.UtcNow.AddDays(1) // Mismo tiempo que Startup.cs OAuthAuthorizationServerOptions
        };

        // Crear el ticket de autenticación
        AuthenticationTicket ticket = new AuthenticationTicket(oAuthIdentity, props);

        // Utilizar el CustomJwtFormat para generar el token
        CustomJwtFormat jwtFormat = new CustomJwtFormat(ConfigurationManager.AppSettings["JwtIssuer"]);
        return jwtFormat.Protect(ticket);
    }


    private class CodigoValidacionTemporal
    {
        public string Codigo { get; set; }
        public DateTime Expira { get; set; }
        public string Email { get; set; }
        public string NIF { get; set; }
    }

}

public class ClientValidationRequest
{
    public string Email { get; set; }
    public string NIF { get; set; }
}

public class CodigoValidacionRequest
{
    public string Email { get; set; }
    public string NIF { get; set; }
    public string Code { get; set; }
    public string TokenForValidation { get; set; }
}

public class CodigoValidacionModel
{
    public string Email { get; set; }
    public string Token { get; set; }
    public string Codigo { get; set; }
}

public class PrestashopLoginRequest
{
    public string Email { get; set; }
    public string Nif { get; set; }
}