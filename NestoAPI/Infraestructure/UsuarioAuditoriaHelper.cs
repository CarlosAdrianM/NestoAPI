using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;
using System.Transactions;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Resuelve el usuario que se graba en los campos de auditoría (Usuario) de las tablas.
    /// </summary>
    /// <remarks>
    /// El usuario debe tomarse SIEMPRE del Identity autenticado, no de lo que mande el cliente
    /// en la petición. El JWT de empleado (endpoint /api/auth/windows-token) lleva el usuario
    /// Windows real (DOMINIO\Usuario), así que <c>User.Identity.Name</c> es la fuente fiable.
    ///
    /// Los clientes (Nesto) venían pasando el usuario en un parámetro de query construido con
    /// <c>Environment.UserDomainName + "\" + Environment.UserName</c>. Cuando Nesto se ejecuta en
    /// contexto de máquina/sistema en el servidor RDS, <c>Environment.UserName</c> devuelve el
    /// machine account del proceso (p. ej. NUEVAVISION\RDS2016$), que acababa grabándose como
    /// autor de la oferta. Además, un parámetro de query es spoofeable. Por eso se ignora salvo
    /// como último recurso cuando no hay Identity (tests o llamadas no autenticadas).
    ///
    /// <para><b>NestoAPI#823 (Carlos 05/11/26): una persona, una identidad.</b> La misma persona entra en Nesto con el
    /// usuario de dominio (<c>NUEVAVISION\Alfredo</c>, windows-token) y en Ariadna/NestoApp con el de Identity, sin dominio
    /// (<c>Alfredo</c>, /oauth/token). Los SPs y las tablas por usuario (FechasPermitidas, triggers, LogModificaciones…)
    /// solo conocen al de dominio, así que desde Ariadna fallaban («Fecha no permitida» al facturar al imprimir la
    /// etiqueta, Santiago 05/11/26). En vez de duplicar filas por usuario, aquí, en el único punto por el que pasa el
    /// usuario que va a los SPs y a las trazas, un nombre SIN dominio que tiene <b>gemelo de dominio</b> se cambia por él
    /// (<see cref="Persona"/>). El gemelo existe si hay fila <c>NUEVAVISION\&lt;nombre&gt;</c> en FechasPermitidas o en
    /// ParámetrosUsuario de la empresa 1 (sin distinguir mayúsculas) y se devuelve con la caja de esa fila
    /// (<c>NUEVAVISION\santiago</c>): SQL Server compara sin caja, pero los triggers y el histórico lo graban tal cual.
    /// Quien no tiene gemelo (clientes de la tienda, RevisionGoogle, usuarios automáticos) se queda como estaba.</para>
    /// <para>Ojo: en ParámetrosUsuario los parámetros de verdad están SIN dominio («Alfredo», como los lee
    /// <c>ParametrosUsuarioController.LeerParametro</c>, que quita el dominio): eso no cambia.</para>
    /// <para>Se consulta como mucho una vez cada <see cref="DURACION_CACHE"/> por nombre. Sin consultor configurado
    /// (las pruebas) no se resuelve nada. Si la consulta falla se devuelve el nombre tal cual (esto nunca rompe una
    /// petición) y se registra en ELMAH una vez.</para>
    /// <para><b>NestoAPI#827 (sugerencia 959; Carlos 05/11/26): una sola grafía por persona.</b> Windows devuelve el usuario
    /// con la caja con la que se tecleó al iniciar sesión y Nesto lo mandaba tal cual: «NUEVAVISION\dlopez» y
    /// «NUEVAVISION\Dlopez» partían a Daniel en dos en el informe de rapports. Ahora <see cref="Persona"/> devuelve todo
    /// usuario de dominio (venga con dominio o resuelto desde el gemelo) como <c>NUEVAVISION\&lt;grafía canónica&gt;</c>:
    /// la del usuario de Identity con ese nombre («DLopez», «MariaJose») o, si no lo hay (o la de Identity está entera en
    /// minúsculas o en mayúsculas, o falla la consulta), la inicial en mayúscula y el resto en minúscula («Santiago», «Reina»,
    /// «Aida»). Cacheada como el gemelo. Solo cambia la caja: SQL compara sin ella, así que FechasPermitidas,
    /// ParámetrosUsuario y los SPs siguen igual. Las cuentas de máquina (<c>RDS2016$</c>) y los nombres con más de una
    /// barra se dejan como vienen. Se enciende con <see cref="UsarBaseDeDatos"/> (Startup) o <see cref="ConfigurarGrafia"/>
    /// (pruebas); con <see cref="Configurar"/> solo, queda apagada (el comportamiento de #823 tal cual).</para>
    /// </remarks>
    public static class UsuarioAuditoriaHelper
    {
        /// <summary>Longitud de las columnas Usuario de auditoría.</summary>
        public const int LONGITUD_MAXIMA = 30;

        /// <summary>Cuando no hay forma de saber quién fue. Mejor esto que una cadena vacía.</summary>
        public const string DESCONOCIDO = "DESCONOCIDO";

        /// <summary>NestoAPI#823: el dominio de los gemelos.</summary>
        public const string DOMINIO = Constantes.Dominios.PRINCIPAL;

        /// <summary>NestoAPI#823: FechasPermitidas.Usuario es nvarchar(60).</summary>
        private const int LONGITUD_GEMELO = 60;

        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);

        /// <summary>
        /// NestoAPI#823: el gemelo de dominio tal como está en la fila, o ninguna fila. FechasPermitidas primero y, dentro, la
        /// empresa 1 (la caja cambia de una empresa a otra: «NUEVAVISION\santiago» en la 1 y «NUEVAVISION\Santiago» en la 3),
        /// para que sea siempre la misma. Parametrizado: el nombre viene del token. Por SQL y no por el EDMX (FechasPermitidas
        /// no está en él).
        /// </summary>
        internal const string SQL_GEMELO =
            "SELECT TOP 1 Usuario FROM (" +
            "SELECT RTRIM(Usuario) AS Usuario, 1 AS Orden, Empresa FROM dbo.FechasPermitidas WITH (NOLOCK) WHERE Usuario = @gemelo " +
            "UNION ALL " +
            "SELECT RTRIM(Usuario) AS Usuario, 2 AS Orden, Empresa FROM dbo.[ParámetrosUsuario] WITH (NOLOCK) WHERE Empresa = '1' AND Usuario = @gemelo" +
            ") t ORDER BY Orden, CASE WHEN Empresa = '1' THEN 0 ELSE 1 END, Empresa";

        private class Entrada
        {
            internal string Resultado;
            internal DateTime CaducaEn;
        }

        private static readonly object candado = new object();
        private static readonly Dictionary<string, Entrada> cache = new Dictionary<string, Entrada>(StringComparer.OrdinalIgnoreCase);
        private static Func<string, string> buscarGemelo;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;
        private static bool errorAvisado;

        /// <summary>NestoAPI#827: grafía canónica (apagada hasta ConfigurarGrafia/UsarBaseDeDatos).</summary>
        private static Func<string, string> buscarEnIdentity;
        private static bool grafiaActiva;
        private static bool errorIdentityAvisado;
        private static readonly Dictionary<string, Entrada> cacheGrafia = new Dictionary<string, Entrada>(StringComparer.OrdinalIgnoreCase);

        /// <summary>NestoAPI#827: AspNetUsers.UserName es nvarchar(256); se compara sin caja (la intercalación de la BD).</summary>
        internal const string SQL_IDENTITY = "SELECT TOP 1 UserName FROM dbo.AspNetUsers WITH (NOLOCK) WHERE UserName = @nombre";

        public static string Resolver(IPrincipal user, string usuarioFallback)
        {
            if (user?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(user.Identity.Name))
            {
                return Persona(user.Identity.Name);
            }

            return Persona(usuarioFallback);
        }

        /// <summary>
        /// NestoAPI#456: deja el usuario listo para grabarlo en una columna de auditoría:
        /// recortado, de 30 caracteres como mucho y nunca vacío.
        ///
        /// <para>Hace falta porque estas columnas son NOT NULL con un valor por defecto
        /// <c>suser_sname()</c>. Mientras el EDMX marcaba la columna como <c>Computed</c>, EF ni
        /// siquiera la mandaba y el valor por defecto tapaba el problema: los 43.286 prepagos de
        /// la tabla salían a nombre de la cuenta de máquina. Al dejar de ser <c>Computed</c>, un
        /// usuario vacío ya no se tapa: rompe contra el NOT NULL. De eso protege esto.</para>
        /// <para>NestoAPI#823: y con el gemelo de dominio si lo tiene (<see cref="Persona"/>).</para>
        /// </summary>
        public static string ParaAuditoria(string usuario)
        {
            string limpio = Persona(usuario)?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return DESCONOCIDO;
            }

            return limpio.Length > LONGITUD_MAXIMA
                ? limpio.Substring(0, LONGITUD_MAXIMA)
                : limpio;
        }

        /// <summary>
        /// NestoAPI#823: la identidad única de la persona. Sin dominio y con gemelo → el gemelo; en cualquier otro caso (un
        /// correo, sin gemelo, sin BD o si falla la consulta), tal cual. Null y vacío se devuelven igual. Nunca lanza.
        /// <para>NestoAPI#827: con la grafía encendida, todo usuario <c>NUEVAVISION\x</c> (el que llega con dominio y el
        /// gemelo) sale con la grafía canónica (<see cref="GrafiaCanonica"/>); apagada, el gemelo con la caja de su fila y el
        /// de dominio tal cual.</para>
        /// </summary>
        public static string Persona(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return usuario;
            }
            string limpio = usuario.Trim();
            if (PuedeTenerGemelo(limpio))
            {
                string gemelo = Gemelo(limpio);
                return gemelo == null ? usuario : ConGrafiaCanonica(gemelo);
            }
            string canonico = ConGrafiaCanonica(limpio);
            // Si no cambia nada, se devuelve lo que vino (con sus espacios), como en #823.
            return string.Equals(canonico, limpio, StringComparison.Ordinal) ? usuario : canonico;
        }

        /// <summary>
        /// NestoAPI#827: «NUEVAVISION\DLopez» a partir de «nuevavision\dlopez» (y así con cualquier caja). Lo que no es un
        /// usuario del dominio principal con un solo nombre detrás (otro dominio, sin dominio, cuenta de máquina con «$», más
        /// de una barra) o con la grafía apagada, tal cual (recortado).
        /// </summary>
        internal static string ConGrafiaCanonica(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio) || !grafiaActiva)
            {
                return limpio;
            }
            int barra = limpio.IndexOf('\\');
            if (barra < 0 || barra != limpio.LastIndexOf('\\')
                || !string.Equals(limpio.Substring(0, barra), DOMINIO, StringComparison.OrdinalIgnoreCase))
            {
                return limpio;
            }
            string nombre = limpio.Substring(barra + 1).Trim();
            if (nombre.Length == 0 || nombre.IndexOf('$') >= 0 || nombre.IndexOf('@') >= 0)
            {
                return limpio;
            }
            return DOMINIO + "\\" + GrafiaCanonica(nombre);
        }

        /// <summary>
        /// NestoAPI#827: la grafía «bonita» de un nombre de usuario (sin dominio): la de su usuario de Identity si la tiene y
        /// tiene forma de nombre («DLopez», «MariaJose»); si no, inicial en mayúscula y el resto en minúscula («Santiago»,
        /// «Reina», «Aida»). Con la grafía apagada, tal cual. Cacheada <see cref="DURACION_CACHE"/>. Nunca lanza: si falla la
        /// consulta a Identity, la regla general (y ELMAH una vez).
        /// </summary>
        internal static string GrafiaCanonica(string nombre)
        {
            string limpio = nombre?.Trim();
            if (string.IsNullOrEmpty(limpio) || !grafiaActiva)
            {
                return limpio;
            }
            DateTime ahora = reloj();
            lock (candado)
            {
                if (cacheGrafia.TryGetValue(limpio, out Entrada entrada) && ahora < entrada.CaducaEn)
                {
                    return entrada.Resultado;
                }
            }
            string deIdentity = null;
            Exception error = null;
            Func<string, string> buscador = buscarEnIdentity;
            if (buscador != null)
            {
                try
                {
                    deIdentity = buscador(limpio)?.Trim();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }
            string resultado = EsGrafiaDeIdentityValida(deIdentity, limpio) ? deIdentity : InicialMayuscula(limpio);
            bool avisar = false;
            lock (candado)
            {
                if (error != null)
                {
                    avisar = !errorIdentityAvisado;
                    errorIdentityAvisado = true;
                }
                cacheGrafia[limpio] = new Entrada { Resultado = resultado, CaducaEn = ahora + DURACION_CACHE };
            }
            if (avisar)
            {
                try
                {
                    registrar?.Invoke(new Exception($"[NestoAPI#827] No se ha podido leer la grafía de «{limpio}» en Identity " +
                        $"(se usa «{resultado}»; no se vuelve a avisar hasta reiniciar): {error.Message}", error));
                }
                catch
                {
                    // El registro nunca puede tumbar una petición.
                }
            }
            return resultado;
        }

        /// <summary>NestoAPI#827: «Santiago» de «SANTIAGO» o «santiago».</summary>
        internal static string InicialMayuscula(string nombre)
        {
            if (string.IsNullOrEmpty(nombre))
            {
                return nombre;
            }
            return char.ToUpperInvariant(nombre[0]) + nombre.Substring(1).ToLowerInvariant();
        }

        /// <summary>
        /// NestoAPI#827: la de Identity vale si es el mismo nombre (sin caja) y tiene forma de nombre: empieza por mayúscula y
        /// no está entera en mayúsculas (un «dlopez» o un «CARLOS» guardado así en Identity se pone bonito con la regla general).
        /// </summary>
        private static bool EsGrafiaDeIdentityValida(string deIdentity, string nombre)
        {
            if (string.IsNullOrEmpty(deIdentity) || !string.Equals(deIdentity, nombre, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!char.IsUpper(deIdentity[0]))
            {
                return false;
            }
            return deIdentity.Length == 1 || !string.Equals(deIdentity, deIdentity.ToUpperInvariant(), StringComparison.Ordinal);
        }

        /// <summary>
        /// NestoAPI#827: el usuario tal como se enseña en un informe por usuario: la persona con su grafía canónica, recortado;
        /// vacío si no hay. Para agrupar, compárese sin caja (<see cref="StringComparer.OrdinalIgnoreCase"/>).
        /// </summary>
        public static string ParaMostrar(string usuario) => Persona(usuario?.Trim())?.Trim() ?? string.Empty;

        /// <summary>
        /// NestoAPI#827: solo la grafía. Un usuario de dominio sale con la canónica («NUEVAVISION\Dlopez» → «NUEVAVISION\DLopez»);
        /// cualquier otro (sin dominio, correo, vacío) se devuelve tal cual, SIN resolver el gemelo. Para los flujos que
        /// grababan el usuario del DTO y donde cambiar de persona no toca (pedidos: lo que se graba en las cabeceras, líneas,
        /// efectos y orígenes sigue siendo quien lo mandó, solo que con una caja).
        /// </summary>
        public static string SoloGrafia(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario) || usuario.IndexOf('\\') < 0)
            {
                return usuario;
            }
            string canonico = ConGrafiaCanonica(usuario);
            return string.Equals(canonico, usuario.Trim(), StringComparison.Ordinal) ? usuario : canonico;
        }

        /// <summary>
        /// NestoAPI#827: si los dos nombres son la misma persona (resueltos con <see cref="Persona"/> y sin distinguir
        /// mayúsculas). Dos vacíos o nulos no son nadie: false.
        /// </summary>
        public static bool MismaPersona(string uno, string otro)
        {
            string a = Persona(uno?.Trim())?.Trim();
            string b = Persona(otro?.Trim())?.Trim();
            return !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// NestoAPI#823: los nombres con los que puede estar grabada la persona: su identidad (<see cref="Persona"/>), su
        /// nombre sin dominio si es un gemelo (lo grabado antes de #823; los dispositivos, registrados con el usuario de
        /// Identity) y el que se ha pasado. Sin repetidos; vacía si no hay usuario.
        /// </summary>
        public static List<string> NombresDeLaPersona(string usuario)
        {
            var nombres = new List<string>();
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return nombres;
            }
            string original = usuario.Trim();
            string persona = Persona(original).Trim();
            nombres.Add(persona);
            string sinDominio = SinDominio(persona);
            // Solo si de verdad son la misma persona: el nombre sin dominio resuelve a este mismo usuario de dominio.
            if (!string.Equals(sinDominio, persona, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Persona(sinDominio)?.Trim(), persona, StringComparison.OrdinalIgnoreCase))
            {
                nombres.Add(sinDominio);
            }
            // Con su caja exacta: en SQL da igual, pero las comparaciones en memoria la distinguen.
            if (!nombres.Contains(original, StringComparer.Ordinal))
            {
                nombres.Add(original);
            }
            return nombres;
        }

        /// <summary>«Alfredo» a partir de «NUEVAVISION\Alfredo»; tal cual (recortado) si no lleva dominio. Null si viene null.</summary>
        public static string SinDominio(string usuario)
        {
            string limpio = usuario?.Trim();
            if (limpio == null)
            {
                return null;
            }
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1) : limpio;
        }

        /// <summary>Vacía la caché: la próxima consulta de cada nombre vuelve a mirar la BD.</summary>
        public static void Invalidar()
        {
            lock (candado)
            {
                cache.Clear();
                cacheGrafia.Clear();
            }
        }

        /// <summary>Startup: busca el gemelo con su propio contexto y enciende la grafía canónica con Identity (NestoAPI#827).</summary>
        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar(BuscarGemeloEnLaBaseDeDatos, registrarError);
            ConfigurarGrafia(BuscarEnIdentity);
        }

        /// <summary>
        /// NestoAPI#827: enciende la grafía canónica con este buscador en Identity (null = sin Identity: solo la regla
        /// general) y vacía su caché. Va después de <see cref="Configurar"/>, que la apaga.
        /// </summary>
        internal static void ConfigurarGrafia(Func<string, string> buscadorIdentity)
        {
            lock (candado)
            {
                buscarEnIdentity = buscadorIdentity;
                grafiaActiva = true;
                cacheGrafia.Clear();
                errorIdentityAvisado = false;
            }
        }

        /// <summary>Pone el consultor (null = sin BD: no se resuelve nada) y vacía la caché y el aviso ya dado.</summary>
        internal static void Configurar(Func<string, string> nuevoBuscador, Action<Exception> registrarError = null,
            Func<DateTime> nuevoReloj = null)
        {
            lock (candado)
            {
                buscarGemelo = nuevoBuscador;
                registrar = registrarError;
                reloj = nuevoReloj ?? (() => DateTime.UtcNow);
                cache.Clear();
                errorAvisado = false;
                // NestoAPI#827: la grafía canónica se enciende aparte (ConfigurarGrafia / UsarBaseDeDatos).
                buscarEnIdentity = null;
                grafiaActiva = false;
                cacheGrafia.Clear();
                errorIdentityAvisado = false;
            }
        }

        /// <summary>Pruebas: vuelve al estado de arranque (sin consultor).</summary>
        internal static void Reiniciar() => Configurar(null);

        /// <summary>Sin dominio, sin @ (los clientes de la tienda entran con el correo) y que quepa con el dominio delante.</summary>
        private static bool PuedeTenerGemelo(string limpio)
            => limpio.IndexOf('\\') < 0 && limpio.IndexOf('@') < 0 && limpio.Length + DOMINIO.Length + 1 <= LONGITUD_GEMELO;

        private static string Gemelo(string sinDominio)
        {
            Func<string, string> buscador = buscarGemelo;
            if (buscador == null)
            {
                return null;
            }
            DateTime ahora = reloj();
            lock (candado)
            {
                if (cache.TryGetValue(sinDominio, out Entrada entrada) && ahora < entrada.CaducaEn)
                {
                    return entrada.Resultado;
                }
            }
            string resultado;
            Exception error = null;
            try
            {
                string fila = buscador(DOMINIO + "\\" + sinDominio)?.Trim();
                resultado = string.IsNullOrEmpty(fila) ? null : fila;
            }
            catch (Exception ex)
            {
                resultado = null;
                error = ex;
            }
            bool avisar = false;
            lock (candado)
            {
                if (error != null)
                {
                    avisar = !errorAvisado;
                    errorAvisado = true;
                }
                // También el fallo se guarda: no se insiste en cada petición, se vuelve a probar al caducar.
                cache[sinDominio] = new Entrada { Resultado = resultado, CaducaEn = ahora + DURACION_CACHE };
            }
            if (avisar)
            {
                try
                {
                    registrar?.Invoke(new Exception($"[NestoAPI#823] No se ha podido buscar el usuario de dominio de «{sinDominio}» " +
                        $"(se usa tal cual; no se vuelve a avisar hasta reiniciar): {error.Message}", error));
                }
                catch
                {
                    // El registro nunca puede tumbar una petición.
                }
            }
            return resultado;
        }

        /// <summary>NestoAPI#827: la grafía del usuario de Identity (AspNetUsers), con su propia conexión y fuera de la transacción.</summary>
        internal static string BuscarEnIdentity(string nombre)
        {
            using (var sinTransaccion = new TransactionScope(TransactionScopeOption.Suppress))
            using (var db = new ApplicationDbContext())
            {
                string fila = db.Database.SqlQuery<string>(SQL_IDENTITY,
                        new SqlParameter("@nombre", System.Data.SqlDbType.NVarChar, 256) { Value = nombre })
                    .FirstOrDefault();
                sinTransaccion.Complete();
                return fila;
            }
        }

        internal static string BuscarGemeloEnLaBaseDeDatos(string gemelo)
        {
            // Con su propia conexión y fuera de la transacción que haya abierta: si no, un TransactionScope del llamador
            // la promovería a distribuida (MSDTC) al abrir una segunda conexión.
            using (var sinTransaccion = new TransactionScope(TransactionScopeOption.Suppress))
            using (var db = new NVEntities())
            {
                string fila = db.Database.SqlQuery<string>(SQL_GEMELO,
                        new SqlParameter("@gemelo", System.Data.SqlDbType.NVarChar, LONGITUD_GEMELO) { Value = gemelo })
                    .FirstOrDefault();
                sinTransaccion.Complete();
                return fila;
            }
        }
    }
}
