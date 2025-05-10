// Usings necesarios
using Microsoft.Owin.Security;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Providers;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

public class AuthController : ApiController
{
    private readonly string SecretKey = ConfigurationManager.AppSettings["as:AudienceSecret"];
    private readonly IGestorClientes _gestorClientes;
    private readonly IServicioCorreoElectronico _servicioCorreo;
    private const int ExpirationMinutes = 10;
    private static readonly Dictionary<string, CodigoValidacionTemporal> codigosEnMemoria = new Dictionary<string, CodigoValidacionTemporal>();


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
        codigosEnMemoria[tokenForValidation] = new CodigoValidacionTemporal
        {
            Codigo = codigo,
            Expira = DateTime.UtcNow.AddMinutes(10),
            Email = email,
            NIF = nif
        };


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
        if (!codigosEnMemoria.TryGetValue(model.Token, out CodigoValidacionTemporal entry))
        {
            return Unauthorized(); // Token no encontrado
        }

        // Verificamos que coincidan email y código
        if (!entry.Email.Equals(model.Email, StringComparison.OrdinalIgnoreCase) ||
            !entry.Codigo.Equals(model.Codigo))
        {
            return Unauthorized(); // Datos incorrectos
        }

        // Opcional: eliminar el código para que solo se use una vez
        _ = codigosEnMemoria.Remove(model.Token);

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

            if (expectedSignature == request.TokenForValidation)
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
            JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
            JwtSecurityToken token = handler.ReadJwtToken(accessToken);

            // Validar expiración manualmente
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



    private async Task<string> BuscarCliente(string email, string nif)
    {
        ClienteDTO cliente = await _gestorClientes.BuscarClientePorEmailNif(email, nif);
        return !(cliente is null) && !string.IsNullOrEmpty(cliente.cliente) ? cliente.cliente : string.Empty;
    }

    private string GenerarCodigo()
    {
        return new Random().Next(100000, 999999).ToString();
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
