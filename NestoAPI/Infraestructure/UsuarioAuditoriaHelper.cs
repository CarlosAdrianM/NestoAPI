using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;
using System.Threading;
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

        /// <summary>
        /// NestoAPI#823: el nombre tal como viene en el token de la petición en curso (sin resolver); lo pone
        /// <see cref="UserSyncHandler"/>. Para las pocas reglas que dependen de la APLICACIÓN y no de la persona (imprimir en
        /// el servidor solo para quien no tiene Nesto delante): «NUEVAVISION\Alfredo» puede venir de Nesto o, resuelto, de Ariadna.
        /// </summary>
        private static readonly AsyncLocal<string> nombreDelToken = new AsyncLocal<string>();

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
        /// NestoAPI#823: la identidad única de la persona. Sin dominio y con gemelo → el gemelo (con la caja de su fila);
        /// en cualquier otro caso (con dominio, un correo, sin gemelo, sin BD o si falla la consulta), tal cual. Null y
        /// vacío se devuelven igual. Nunca lanza.
        /// </summary>
        public static string Persona(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return usuario;
            }
            string limpio = usuario.Trim();
            if (!PuedeTenerGemelo(limpio))
            {
                return usuario;
            }
            return Gemelo(limpio) ?? usuario;
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

        /// <summary>NestoAPI#823: lo pone <see cref="UserSyncHandler"/> al empezar cada petición.</summary>
        public static void FijarNombreDelToken(string nombre) => nombreDelToken.Value = nombre;

        /// <summary>
        /// NestoAPI#823: el nombre del token de la petición en curso, SIN resolver (para reglas por aplicación); fuera de una
        /// petición (jobs, pruebas), <paramref name="siNoHay"/>.
        /// </summary>
        public static string NombreDelTokenO(string siNoHay)
        {
            string delToken = nombreDelToken.Value;
            return string.IsNullOrWhiteSpace(delToken) ? siNoHay : delToken;
        }

        /// <summary>Vacía la caché: la próxima consulta de cada nombre vuelve a mirar la BD.</summary>
        public static void Invalidar()
        {
            lock (candado)
            {
                cache.Clear();
            }
        }

        /// <summary>Startup: busca el gemelo con su propio contexto.</summary>
        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar(BuscarGemeloEnLaBaseDeDatos, registrarError);
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
