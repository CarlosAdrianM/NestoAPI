using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>Un enlace de <c>dbo.ClientesAnteriores</c>: el cliente nuevo y el número que tenía antes del cambio de forma fiscal.</summary>
    public class EnlaceClienteAnterior
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string ClienteAnterior { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>Lo que hace falta saber del cliente anterior para validarlo.</summary>
    public class DatosClienteAnterior
    {
        public string Cliente { get; set; }
        public string Nif { get; set; }
    }

    /// <summary>
    /// NestoAPI#876 corte 1 (Carlos 17/11/26): un cliente que ya lo era cambia de forma fiscal (otra razón social o NIF) y se le da de
    /// alta con número nuevo y origen «CAMBIO-FISCAL». Fiscalmente es otro cliente; comercialmente es el mismo. LA REGLA (pura) del alta:
    /// con ese origen el número de cliente anterior es obligatorio, tiene que existir y no puede ser él mismo; sin ese origen no se
    /// admite; el mismo NIF solo se avisa. Nada fiscal se hereda.
    /// </summary>
    public static class ReglaClienteAnterior
    {
        public const string ORIGEN_CAMBIO_FISCAL = "CAMBIO-FISCAL";
        public const string MOTIVO_FALTA = "Con el origen «Cambio de forma fiscal» indique el número de cliente que tenía antes: " +
            "es el mismo cliente con otros datos fiscales.";
        public const string MOTIVO_SIN_CAMBIO_FISCAL = "El número de cliente anterior solo se indica con el origen «Cambio de forma fiscal» (CAMBIO-FISCAL).";

        public static bool EsCambioFiscal(string origen)
            => string.Equals(origen?.Trim(), ORIGEN_CAMBIO_FISCAL, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Null si vale (o si no hay nada que validar: ni origen de cambio fiscal ni anterior). <paramref name="clienteNuevo"/> es el
        /// número del alta si ya se conoce (alta de un contacto de un cliente existente); <paramref name="anterior"/>, lo leído de la
        /// ficha del anterior (null = no existe).
        /// </summary>
        public static string MotivoNoValido(string origen, string clienteAnterior, string clienteNuevo, string empresa, DatosClienteAnterior anterior)
        {
            string numero = clienteAnterior?.Trim();
            bool cambioFiscal = EsCambioFiscal(origen);
            if (string.IsNullOrEmpty(numero))
            {
                return cambioFiscal ? MOTIVO_FALTA : null;
            }
            if (!cambioFiscal)
            {
                return MOTIVO_SIN_CAMBIO_FISCAL;
            }
            if (!string.IsNullOrWhiteSpace(clienteNuevo) && string.Equals(clienteNuevo.Trim(), numero, StringComparison.OrdinalIgnoreCase))
            {
                return $"El cliente anterior no puede ser el mismo cliente ({numero}).";
            }
            if (anterior == null)
            {
                return $"No existe el cliente {numero} en la empresa {empresa?.Trim()}: compruebe el número de cliente anterior.";
            }
            return null;
        }

        /// <summary>Un cambio de forma fiscal suele traer NIF nuevo: si es el mismo, se avisa (no se frena). Null si no hay nada que decir.</summary>
        public static string AvisoMismoNif(DatosClienteAnterior anterior, string nifNuevo)
        {
            string a = Normalizar(anterior?.Nif);
            string b = Normalizar(nifNuevo);
            return a == null || b == null || a != b ? null
                : $"El cliente anterior {anterior.Cliente?.Trim()} tiene el mismo NIF ({b}): en un cambio de forma fiscal suele cambiar. Compruébelo.";
        }

        private static string Normalizar(string nif)
        {
            string limpio = new string((nif ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return limpio.Length == 0 ? null : limpio;
        }
    }

    /// <summary>
    /// NestoAPI#876 corte 2: LA PIEZA ÚNICA «clientes equivalentes» = el cliente y sus anteriores, en cadena (el anterior del anterior…),
    /// sin ciclos. La usarán las consultas COMERCIALES (plantilla de venta, rapports, sugerencias de llamadas, fechas de compras,
    /// embudo: cortes 3 y 4); NUNCA las fiscales ni contables (facturas, extracto, deuda, remesas, 347, Verifactu, riesgo).
    /// </summary>
    public interface IClientesEquivalentes
    {
        /// <summary>El cliente (primero) y sus anteriores, del más reciente al más antiguo, sin relleno. Nunca vacía con cliente; nunca lanza.</summary>
        Task<IReadOnlyList<string>> De(string empresa, string cliente);
    }

    /// <summary>La regla (pura) de la cadena: sigue los enlaces cliente → anterior hasta que no hay más, se repite uno o llega al tope.</summary>
    public static class ReglaClientesEquivalentes
    {
        /// <summary>Tope de saltos (a nadie le cambian la forma fiscal veinte veces): un dato raro nunca deja la consulta colgada.</summary>
        public const int MAXIMO_SALTOS = 20;

        public static IReadOnlyList<string> Cadena(string cliente, IReadOnlyDictionary<string, string> anteriorDe)
        {
            string actual = cliente?.Trim();
            if (string.IsNullOrEmpty(actual))
            {
                return new List<string>();
            }
            var cadena = new List<string> { actual };
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { actual };
            for (int i = 0; i < MAXIMO_SALTOS && anteriorDe != null && anteriorDe.TryGetValue(actual, out string anterior); i++)
            {
                anterior = anterior?.Trim();
                if (string.IsNullOrEmpty(anterior) || !vistos.Add(anterior))
                {
                    break;
                }
                cadena.Add(anterior);
                actual = anterior;
            }
            return cadena;
        }

        /// <summary>Los enlaces de una empresa como diccionario cliente → anterior (sin relleno; si se repitiera un cliente, el más reciente).</summary>
        public static IReadOnlyDictionary<string, string> Diccionario(IEnumerable<EnlaceClienteAnterior> enlaces)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (EnlaceClienteAnterior e in (enlaces ?? Enumerable.Empty<EnlaceClienteAnterior>())
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Cliente) && !string.IsNullOrWhiteSpace(e.ClienteAnterior))
                .OrderBy(e => e.Fecha))
            {
                d[e.Cliente.Trim()] = e.ClienteAnterior.Trim();
            }
            return d;
        }
    }

    /// <summary>
    /// <c>dbo.ClientesAnteriores</c> (fuera del EDMX, <c>Scripts/Issue876_ClientesAnteriores.sql</c>). Sin la tabla: no hay enlaces, el
    /// alta no exige el anterior y nada se guarda.
    /// </summary>
    public interface IRepositorioClientesAnteriores
    {
        bool TablaExiste();
        /// <summary>El número anterior del cliente, o null.</summary>
        Task<string> LeerAnterior(string empresa, string cliente);
        /// <summary>Todos los enlaces de la empresa (es una tabla pequeña).</summary>
        Task<List<EnlaceClienteAnterior>> LeerEnlaces(string empresa);
        /// <summary>La ficha del cliente (su NIF), o null si no existe.</summary>
        Task<DatosClienteAnterior> LeerCliente(string empresa, string cliente);
        /// <summary>Pone (o cambia) el anterior del cliente y deja la fila en Modificaciones (Tabla «ClientesAnteriores»).</summary>
        Task Guardar(string empresa, string cliente, string clienteAnterior, string usuario);
    }

    public class RepositorioClientesAnterioresSql : IRepositorioClientesAnteriores
    {
        internal const string TABLA = "ClientesAnteriores";

        internal const string SQL_ANTERIOR = "IF OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NOT NULL " +
            "SELECT RTRIM(ClienteAnterior) FROM dbo.ClientesAnteriores WITH (NOLOCK) WHERE Empresa = @empresa AND Cliente = @cliente";

        /// <summary>El anterior leído dentro de una transacción (la copia de condiciones), bloqueando la fila hasta el final.</summary>
        internal const string SQL_ANTERIOR_BLOQUEANDO = "IF OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NOT NULL " +
            "SELECT RTRIM(ClienteAnterior) FROM dbo.ClientesAnteriores WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Cliente = @cliente";

        internal const string SQL_ENLACES = "IF OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NOT NULL " +
            "SELECT RTRIM(Empresa) AS Empresa, RTRIM(Cliente) AS Cliente, RTRIM(ClienteAnterior) AS ClienteAnterior, Fecha, RTRIM(Usuario) AS Usuario " +
            "FROM dbo.ClientesAnteriores WITH (NOLOCK) WHERE Empresa = @empresa";

        internal const string SQL_CLIENTE = "SELECT TOP 1 RTRIM([Nº Cliente]) AS Cliente, RTRIM([CIF/NIF]) AS Nif FROM Clientes WITH (NOLOCK) " +
            "WHERE Empresa = @empresa AND [Nº Cliente] = @cliente ORDER BY ClientePrincipal DESC, Contacto";

        /// <summary>El enlace de la copia de condiciones (#876, dentro de su transacción): solo si el cliente aún no tiene anterior.</summary>
        internal const string SQL_CREAR_SI_NO_HAY = @"
IF NOT EXISTS (SELECT 1 FROM dbo.ClientesAnteriores WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Cliente = @cliente)
    INSERT INTO dbo.ClientesAnteriores (Empresa, Cliente, ClienteAnterior, Fecha, Usuario) VALUES (@empresa, @cliente, @anterior, GETDATE(), ISNULL(@usuario, N'(desconocido)'));";

        internal const string SQL_GUARDAR = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @antes char(10) = (SELECT ClienteAnterior FROM dbo.ClientesAnteriores WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Cliente = @cliente);
IF @antes IS NULL
    INSERT INTO dbo.ClientesAnteriores (Empresa, Cliente, ClienteAnterior, Fecha, Usuario) VALUES (@empresa, @cliente, @anterior, GETDATE(), @usuario);
ELSE IF RTRIM(@antes) <> RTRIM(@anterior)
    UPDATE dbo.ClientesAnteriores SET ClienteAnterior = @anterior, Fecha = GETDATE(), Usuario = @usuario WHERE Empresa = @empresa AND Cliente = @cliente;
IF ISNULL(RTRIM(@antes), '') <> RTRIM(@anterior)
    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
    VALUES (N'ClientesAnteriores', CONCAT(RTRIM(@cliente), N': ', ISNULL(RTRIM(@antes), N'sin anterior')), CONCAT(RTRIM(@cliente), N': ', RTRIM(@anterior)), @usuario);
COMMIT TRANSACTION;";

        private readonly Func<NVEntities> crearContexto;

        public RepositorioClientesAnterioresSql(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
        }

        public bool TablaExiste()
        {
            using (NVEntities db = crearContexto())
            {
                return ColumnasOpcionales.ExisteTabla(db, TABLA);
            }
        }

        public async Task<string> LeerAnterior(string empresa, string cliente)
        {
            using (NVEntities db = crearContexto())
            {
                List<string> filas = await db.Database.SqlQuery<string>(SQL_ANTERIOR, Char("@empresa", empresa, 3), Char("@cliente", cliente, 10))
                    .ToListAsync().ConfigureAwait(false);
                string anterior = filas.FirstOrDefault();
                return string.IsNullOrWhiteSpace(anterior) ? null : anterior.Trim();
            }
        }

        public async Task<List<EnlaceClienteAnterior>> LeerEnlaces(string empresa)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<EnlaceClienteAnterior>(SQL_ENLACES, Char("@empresa", empresa, 3)).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<DatosClienteAnterior> LeerCliente(string empresa, string cliente)
        {
            using (NVEntities db = crearContexto())
            {
                List<DatosClienteAnterior> filas = await db.Database.SqlQuery<DatosClienteAnterior>(SQL_CLIENTE, Char("@empresa", empresa, 3), Char("@cliente", cliente, 10))
                    .ToListAsync().ConfigureAwait(false);
                return filas.FirstOrDefault();
            }
        }

        public async Task Guardar(string empresa, string cliente, string clienteAnterior, string usuario)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(TransactionalBehavior.DoNotEnsureTransaction, SQL_GUARDAR,
                    Char("@empresa", empresa, 3), Char("@cliente", cliente, 10), Char("@anterior", clienteAnterior, 10),
                    new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) }).ConfigureAwait(false);
            }
            ClientesEquivalentes.Invalidar();
        }

        private static SqlParameter Char(string nombre, string valor, int largo)
            => new SqlParameter(nombre, SqlDbType.Char, largo) { Value = (object)valor?.Trim() ?? DBNull.Value };
    }

    /// <summary>
    /// <see cref="IClientesEquivalentes"/> con caché corta (5 min por empresa, como las demás del repo): los enlaces de la empresa se leen
    /// de una vez y la cadena se resuelve en memoria (<see cref="ReglaClientesEquivalentes"/>). Si no se puede leer (sin tabla, fallo):
    /// solo el cliente, ELMAH una vez y se reintenta al minuto. Guardar un enlace invalida la caché.
    /// </summary>
    public class ClientesEquivalentes : IClientesEquivalentes
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan REINTENTO = TimeSpan.FromMinutes(1);

        private static readonly object candado = new object();
        private static readonly Dictionary<string, (IReadOnlyDictionary<string, string> Enlaces, DateTime CaducaEn)> cache =
            new Dictionary<string, (IReadOnlyDictionary<string, string>, DateTime)>(StringComparer.OrdinalIgnoreCase);
        private static bool errorAvisado;

        private readonly IRepositorioClientesAnteriores repositorio;
        private readonly Func<DateTime> ahora;

        public ClientesEquivalentes(IRepositorioClientesAnteriores repositorio = null, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? new RepositorioClientesAnterioresSql();
            this.ahora = ahora ?? (() => DateTime.UtcNow);
        }

        /// <summary>La siguiente consulta vuelve a la BD (al guardar un enlace; y en los tests).</summary>
        public static void Invalidar()
        {
            lock (candado)
            {
                cache.Clear();
            }
        }

        public async Task<IReadOnlyList<string>> De(string empresa, string cliente)
        {
            string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            IReadOnlyDictionary<string, string> enlaces = await Enlaces(emp).ConfigureAwait(false);
            return ReglaClientesEquivalentes.Cadena(cliente, enlaces);
        }

        private async Task<IReadOnlyDictionary<string, string>> Enlaces(string empresa)
        {
            DateTime momento = ahora();
            lock (candado)
            {
                if (cache.TryGetValue(empresa, out var guardado) && momento < guardado.CaducaEn)
                {
                    return guardado.Enlaces;
                }
            }
            try
            {
                IReadOnlyDictionary<string, string> leidos = repositorio.TablaExiste()
                    ? ReglaClientesEquivalentes.Diccionario(await repositorio.LeerEnlaces(empresa).ConfigureAwait(false))
                    : new Dictionary<string, string>();
                lock (candado)
                {
                    cache[empresa] = (leidos, momento + DURACION_CACHE);
                }
                return leidos;
            }
            catch (Exception ex)
            {
                if (!errorAvisado)
                {
                    errorAvisado = true;
                    ElmahHelper.Log(new Exception($"[Clientes equivalentes #876] No se han podido leer los clientes anteriores: {ex.Message}", ex));
                }
                IReadOnlyDictionary<string, string> vacio = new Dictionary<string, string>();
                lock (candado)
                {
                    cache[empresa] = (vacio, momento + REINTENTO);
                }
                return vacio;
            }
        }
    }
}
