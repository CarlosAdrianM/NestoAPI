using NestoAPI.Infraestructure.Prestashop;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NestoAPI.Infraestructure.Cupones
{
    /// <summary>NestoAPI#614: cómo está el cupón en PrestaShop (Cupones.EstadoEspejo). Null: nunca se ha llevado.</summary>
    public static class EstadosEspejo
    {
        /// <summary>Hay un cart_rule activo con el código del cupón.</summary>
        public const string ACTIVO = "Activo";
        /// <summary>El cart_rule existe, desactivado (active = 0, quantity = 0); o no hacía falta crearlo.</summary>
        public const string INACTIVO = "Inactivo";
        /// <summary>El cliente no tiene cuenta en la tienda (ninguna dirección con su NIF casa con él): se vuelve a mirar cada día.</summary>
        public const string SIN_CLIENTE = "SinCliente";
    }

    /// <summary>NestoAPI#614: un cupón con lo que hace falta para llevarlo a PrestaShop.</summary>
    public class CuponEspejo : CuponLeido
    {
        public int? IdCartRulePrestashop { get; set; }
        public int? IdCustomerPrestashop { get; set; }
        public string EstadoEspejo { get; set; }
        public DateTime? EspejoPendienteDesde { get; set; }
        public int EspejoIntentos { get; set; }
        public string EspejoError { get; set; }
        public DateTime? FechaEspejo { get; set; }
        public DateTime? FechaAvisoEspejo { get; set; }
    }

    /// <summary>Lo que ha pasado al llevar un cupón a PrestaShop.</summary>
    public class ResultadoEspejo
    {
        public int CuponId { get; set; }
        public bool Correcto { get; set; }
        public string EstadoEspejo { get; set; }
        public int? IdCartRule { get; set; }
        public int? IdCustomer { get; set; }
        /// <summary>Con <see cref="Correcto"/> false, por qué ha fallado; con SinCliente, el aviso.</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// NestoAPI#614 (corte 4 de #610): las reglas del espejo de los cupones en PrestaShop, sin red ni base de datos. Nesto es el
    /// dueño de los cupones y PrestaShop un espejo (Carlos, 08/10/26): un cupón que se puede usar (Emitido o Activo, sin caducar)
    /// tiene un cart_rule activo con su código; uno usado, anulado o caducado, desactivado.
    /// </summary>
    public static class ReglasEspejoPrestashop
    {
        /// <summary>Interruptor (Web.config), nace apagado: con él apagado no se llama a PrestaShop ni se acepta el uso desde la tienda.</summary>
        public const string CLAVE_INTERRUPTOR = "Cupones:EspejoPrestashop";
        public const string CLAVE_IDIOMA = "Cupones:PrestashopIdIdioma";
        public const string CLAVE_MONEDA = "Cupones:PrestashopIdMoneda";
        public const string CLAVE_HORAS_AVISO = "Cupones:HorasAvisoEspejo";
        public const int HORAS_AVISO_POR_DEFECTO = 4;
        /// <summary>Un cliente sin cuenta en la tienda se vuelve a buscar una vez al día (puede abrirla después).</summary>
        public const int HORAS_REINTENTO_SIN_CLIENTE = 24;
        public const string CANAL_PRESTASHOP = "PrestaShop";
        private const int LONGITUD_NOMBRE = 254;

        public static bool Encendido =>
            string.Equals(ConfigurationManager.AppSettings[CLAVE_INTERRUPTOR], "true", StringComparison.OrdinalIgnoreCase);

        public static int IdIdioma => Entero(CLAVE_IDIOMA, 1);

        public static int IdMoneda => Entero(CLAVE_MONEDA, 1);

        public static int HorasAviso => Entero(CLAVE_HORAS_AVISO, HORAS_AVISO_POR_DEFECTO);

        private static int Entero(string clave, int porDefecto) =>
            int.TryParse(ConfigurationManager.AppSettings[clave], out int valor) && valor > 0 ? valor : porDefecto;

        /// <summary>Lo que tiene que haber en PrestaShop: Activo si el cupón se puede usar hoy o se podrá (Emitido); si no, Inactivo.</summary>
        public static string EstadoDeseado(CuponLeido cupon, DateTime hoy)
        {
            string estado = ReglasCupon.EstadoEfectivo(cupon, hoy);
            return estado == EstadosCupon.ACTIVO || estado == EstadosCupon.EMITIDO ? EstadosEspejo.ACTIVO : EstadosEspejo.INACTIVO;
        }

        /// <summary>
        /// ¿Hay que tocar PrestaShop? Crear o reactivar si se puede usar y no está activo allí (un cliente sin cuenta, una vez al
        /// día); desactivar si está activo allí y ya no se puede usar. Uno que nunca llegó a PrestaShop y ya no vale, no se crea.
        /// </summary>
        public static bool HayQueSincronizar(CuponEspejo cupon, DateTime ahora)
        {
            string espejo = cupon.EstadoEspejo?.Trim();
            if (EstadoDeseado(cupon, ahora) == EstadosEspejo.ACTIVO)
            {
                if (espejo == EstadosEspejo.ACTIVO)
                {
                    return false;
                }
                return espejo != EstadosEspejo.SIN_CLIENTE || cupon.FechaEspejo == null
                    || cupon.FechaEspejo.Value.AddHours(HORAS_REINTENTO_SIN_CLIENTE) <= ahora;
            }
            return espejo == EstadosEspejo.ACTIVO;
        }

        public static string Nombre(CuponLeido cupon)
        {
            string evento = string.IsNullOrWhiteSpace(cupon.Evento) ? null : cupon.Evento.Trim();
            string nombre = evento == null ? $"Cupón {cupon.Codigo?.Trim()}" : $"Cupón {cupon.Codigo?.Trim()} ({evento})";
            return nombre.Length > LONGITUD_NOMBRE ? nombre.Substring(0, LONGITUD_NOMBRE) : nombre;
        }

        internal static string Descripcion(CuponLeido cupon) =>
            $"NestoAPI#614: cupón {cupon.Codigo?.Trim()} del cliente {cupon.Cliente?.Trim()} (Nesto es el dueño; no lo modifique aquí: " +
            "los cambios se pierden).";

        private static string Importe(decimal importe) => Math.Round(importe, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>
        /// El cart_rule del cupón (épica #610 y #614): de una vez (quantity 1, quantity_per_user 1, sin uso parcial, que no deja otro
        /// vale con lo que sobre), solo para su customer, importe IVA incluido, válido del día del evento a la caducidad, pedido
        /// mínimo el del cupón (<see cref="ReglasCupon.PedidoMinimo(CuponLeido)"/>: el del evento, 0 = sin mínimo) IVA incluido sin
        /// portes. Combina con otras reglas (el cheque regalo NVCHEQUE-*: Carlos, 15/10/26).
        /// Desactivado: active 0 y quantity 0. Con <paramref name="idCartRule"/>, para el PUT (el recurso entero).
        /// <para>Restricción a productos (Carlos, 22/10/26: que no aplique sobre los cursos ni cuenten para el mínimo): NO se puede
        /// hacer por el webservice. El recurso cart_rules solo expone el indicador product_restriction; las reglas de producto o
        /// categoría (cart_rule_product_rule_group / _rule / _value) no son recursos del webservice ni van como asociaciones del
        /// cart_rule, así que con product_restriction = 1 y sin grupos el cupón no valdría para nada. Hace falta un endpoint en un
        /// módulo propio de la tienda que cree el grupo «categoría de cursos excluida» del cart_rule; mientras tanto va sin
        /// restricción y Nesto, al importar el pedido (Nesto#518), aplica las reglas del corte 3 (solo productos, mínimo sin
        /// cursos ni portes).</para>
        /// </summary>
        public static string XmlCartRule(CuponLeido cupon, int idCustomer, bool activo, int? idCartRule, int idIdioma, int idMoneda)
        {
            XNamespace xlink = "http://www.w3.org/1999/xlink";
            var regla = new XElement("cart_rule");
            if (idCartRule.HasValue)
            {
                regla.Add(new XElement("id", idCartRule.Value));
            }
            regla.Add(
                new XElement("id_customer", idCustomer),
                new XElement("date_from", cupon.ValidoDesde.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                new XElement("date_to", cupon.Caducidad.Date.AddDays(1).AddSeconds(-1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                new XElement("description", Descripcion(cupon)),
                new XElement("quantity", activo ? 1 : 0),
                new XElement("quantity_per_user", 1),
                new XElement("priority", 1),
                new XElement("partial_use", 0),
                new XElement("code", cupon.Codigo?.Trim()),
                new XElement("minimum_amount", Importe(ReglasCupon.PedidoMinimo(cupon))),
                new XElement("minimum_amount_tax", 1),
                new XElement("minimum_amount_currency", idMoneda),
                new XElement("minimum_amount_shipping", 0),
                new XElement("country_restriction", 0),
                new XElement("carrier_restriction", 0),
                new XElement("group_restriction", 0),
                new XElement("cart_rule_restriction", 0),
                new XElement("product_restriction", 0),
                new XElement("shop_restriction", 0),
                new XElement("free_shipping", 0),
                new XElement("reduction_percent", "0.00"),
                new XElement("reduction_amount", Importe(cupon.Importe)),
                new XElement("reduction_tax", 1),
                new XElement("reduction_currency", idMoneda),
                new XElement("reduction_product", 0),
                new XElement("reduction_exclude_special", 0),
                new XElement("gift_product", 0),
                new XElement("gift_product_attribute", 0),
                new XElement("highlight", 1),
                new XElement("active", activo ? 1 : 0),
                new XElement("name", new XElement("language", new XAttribute("id", idIdioma), Nombre(cupon))));
            var documento = new XDocument(new XElement("prestashop", new XAttribute(XNamespace.Xmlns + "xlink", xlink), regla));
            return documento.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>El id del primer &lt;elemento&gt; del XML (el cart_rule creado, o el primero de una lista). Null si no hay.</summary>
        public static int? LeerId(string xml, string elemento)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return null;
            }
            XElement nodo = XDocument.Parse(xml.Trim()).Descendants(elemento).FirstOrDefault();
            if (nodo == null)
            {
                return null;
            }
            string id = nodo.Element("id")?.Value ?? nodo.Attribute("id")?.Value;
            return int.TryParse(id?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int valor) && valor > 0 ? valor : (int?)null;
        }

        /// <summary>«filter[code]=[...]»: el valor entre corchetes y escapado (un + es un espacio si no).</summary>
        internal static string Filtro(string campo, string valor) => $"filter[{campo}]=[{Uri.EscapeDataString(valor ?? string.Empty)}]";
    }

    /// <summary>NestoAPI#614: lo que el resto de la API le pide al espejo. Nunca lanza; con el interruptor apagado no hace nada.</summary>
    public interface IEspejoCupones
    {
        /// <summary>Lleva el cupón a PrestaShop ya (al emitirlo, anularlo o usarlo desde la tienda). Si falla, lo reintenta el job.</summary>
        Task Sincronizar(int cuponId);
        /// <summary>
        /// Dentro de la transacción del pedido, antes de confirmarlo (#614): si el cupón está activo en PrestaShop, lo desactiva.
        /// Devuelve lo que hay que apuntar después (<see cref="ApuntarResultado"/>), o null si no había nada que hacer.
        /// </summary>
        Task<ResultadoEspejo> DesactivarAntesDeConfirmar(int cuponId);
        /// <summary>Después de confirmar (o deshacer) el pedido: si se deshizo, el job lo vuelve a activar en PrestaShop.</summary>
        Task ApuntarResultado(ResultadoEspejo resultado);
    }

    /// <summary>Con el espejo apagado, o en los tests que no lo miran.</summary>
    public class EspejoCuponesApagado : IEspejoCupones
    {
        public static readonly EspejoCuponesApagado Instancia = new EspejoCuponesApagado();

        public Task Sincronizar(int cuponId) => Task.CompletedTask;

        public Task<ResultadoEspejo> DesactivarAntesDeConfirmar(int cuponId) => Task.FromResult<ResultadoEspejo>(null);

        public Task ApuntarResultado(ResultadoEspejo resultado) => Task.CompletedTask;
    }

    public interface IRepositorioEspejoCupones
    {
        Task<CuponEspejo> Leer(int id);
        /// <summary>Los que hay que llevar a PrestaShop (ReglasEspejoPrestashop.HayQueSincronizar, preseleccionados en SQL).</summary>
        Task<List<CuponEspejo>> LeerPendientes(int maximo);
        Task Guardar(ResultadoEspejo resultado);
        /// <summary>Los que llevan más de <paramref name="horas"/> sin poder llevarse a PrestaShop y de los que todavía no se ha avisado.</summary>
        Task<List<CuponEspejo>> LeerRetrasados(int horas);
        Task MarcarAvisados(IEnumerable<int> ids);
        /// <summary>El NIF del cliente (el del contacto principal; si no, el de cualquiera), o null.</summary>
        Task<string> LeerNifCliente(string empresa, string cliente);
    }

    /// <summary>
    /// NestoAPI#614: las columnas del espejo en dbo.Cupones (Scripts/Issue614_CuponesPrestashop.sql), por SQL directo como el resto de
    /// la tabla. Solo se leen con el interruptor encendido: el script va ANTES de encenderlo.
    /// </summary>
    public class RepositorioEspejoCupones : IRepositorioEspejoCupones
    {
        private const string COLUMNAS_ESPEJO = @", c.IdCartRulePrestashop, c.IdCustomerPrestashop, c.EstadoEspejo, c.EspejoPendienteDesde,
       c.EspejoIntentos, c.EspejoError, c.FechaEspejo, c.FechaAvisoEspejo ";

        internal const string SQL_LEER = "SELECT " + RepositorioCupones.LISTA_COLUMNAS + COLUMNAS_ESPEJO + RepositorioCupones.ORIGEN + " WHERE c.Id = @p0";

        // Activo (d.Activo = 1) es lo mismo que ReglasCupon.EstadoEfectivo en Activo o Emitido: sin caducar y libre (también el Usado
        // cuyo pedido ya no tiene la línea). En C# se vuelve a mirar con ReglasEspejoPrestashop.HayQueSincronizar.
        internal const string SQL_PENDIENTES = "SELECT TOP (@p0) " + RepositorioCupones.LISTA_COLUMNAS + COLUMNAS_ESPEJO + RepositorioCupones.ORIGEN + @"
CROSS APPLY (SELECT CASE WHEN c.Caducidad >= CAST(GETDATE() AS date)
                          AND (c.Estado IN ('Emitido', 'Activo')
                               OR (c.Estado = 'Usado' AND NOT EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = c.UsadoEnPedido AND l.Producto = @prod)))
                         THEN 1 ELSE 0 END AS Activo) d
WHERE (d.Activo = 1 AND ISNULL(c.EstadoEspejo, '') <> 'Activo'
       AND (ISNULL(c.EstadoEspejo, '') <> 'SinCliente' OR c.FechaEspejo IS NULL OR c.FechaEspejo <= DATEADD(hour, -24, GETDATE())))
   OR (d.Activo = 0 AND c.EstadoEspejo = 'Activo')
ORDER BY c.Id";

        internal const string SQL_RETRASADOS = "SELECT " + RepositorioCupones.LISTA_COLUMNAS + COLUMNAS_ESPEJO + RepositorioCupones.ORIGEN + @"
WHERE c.EspejoPendienteDesde <= DATEADD(hour, -@p0, GETDATE()) AND c.FechaAvisoEspejo IS NULL";

        internal const string SQL_GUARDAR_BIEN = @"
UPDATE dbo.Cupones
SET IdCartRulePrestashop = ISNULL(@p1, IdCartRulePrestashop), IdCustomerPrestashop = ISNULL(@p2, IdCustomerPrestashop), EstadoEspejo = @p3,
    EspejoError = @p4, FechaEspejo = GETDATE(), EspejoIntentos = 0, EspejoPendienteDesde = NULL, FechaAvisoEspejo = NULL
WHERE Id = @p0";

        internal const string SQL_GUARDAR_MAL = @"
UPDATE dbo.Cupones
SET IdCartRulePrestashop = ISNULL(@p1, IdCartRulePrestashop), IdCustomerPrestashop = ISNULL(@p2, IdCustomerPrestashop),
    EspejoError = @p4, EspejoIntentos = EspejoIntentos + 1, EspejoPendienteDesde = ISNULL(EspejoPendienteDesde, GETDATE())
WHERE Id = @p0";

        private readonly NVEntities db;
        private readonly string productoCupon;

        public RepositorioEspejoCupones(NVEntities db, string productoCupon = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.productoCupon = productoCupon ?? ReglasCupon.ProductoCupon;
        }

        public async Task<CuponEspejo> Leer(int id) =>
            (await db.Database.SqlQuery<CuponEspejo>(SQL_LEER, new SqlParameter("@p0", id), new SqlParameter("@prod", productoCupon))
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<List<CuponEspejo>> LeerPendientes(int maximo) =>
            await db.Database.SqlQuery<CuponEspejo>(SQL_PENDIENTES, new SqlParameter("@p0", maximo), new SqlParameter("@prod", productoCupon))
                .ToListAsync().ConfigureAwait(false);

        public async Task Guardar(ResultadoEspejo r) =>
            _ = await db.Database.ExecuteSqlCommandAsync(r.Correcto ? SQL_GUARDAR_BIEN : SQL_GUARDAR_MAL,
                new SqlParameter("@p0", r.CuponId),
                new SqlParameter("@p1", (object)r.IdCartRule ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.Int },
                new SqlParameter("@p2", (object)r.IdCustomer ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.Int },
                new SqlParameter("@p3", (object)r.EstadoEspejo ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.VarChar, Size = 12 },
                new SqlParameter("@p4", (object)Recortar(r.Error, 500) ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.NVarChar, Size = 500 })
                .ConfigureAwait(false);

        public async Task<List<CuponEspejo>> LeerRetrasados(int horas) =>
            await db.Database.SqlQuery<CuponEspejo>(SQL_RETRASADOS, new SqlParameter("@p0", horas), new SqlParameter("@prod", productoCupon))
                .ToListAsync().ConfigureAwait(false);

        public async Task MarcarAvisados(IEnumerable<int> ids)
        {
            foreach (int id in (ids ?? Enumerable.Empty<int>()).Distinct())
            {
                _ = await db.Database.ExecuteSqlCommandAsync("UPDATE dbo.Cupones SET FechaAvisoEspejo = GETDATE() WHERE Id = @p0",
                    new SqlParameter("@p0", id)).ConfigureAwait(false);
            }
        }

        public async Task<string> LeerNifCliente(string empresa, string cliente)
        {
            string e = empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string c = cliente?.Trim() ?? string.Empty;
            var fichas = await db.Clientes.Where(x => x.Empresa == e && x.Nº_Cliente == c && x.CIF_NIF != null)
                .Select(x => new { x.ClientePrincipal, x.CIF_NIF }).ToListAsync().ConfigureAwait(false);
            return fichas.OrderBy(x => x.ClientePrincipal ? 0 : 1).Select(x => x.CIF_NIF.Trim()).FirstOrDefault(n => n.Length > 0);
        }

        private static string Recortar(string texto, int max) =>
            string.IsNullOrEmpty(texto) ? texto : texto.Length > max ? texto.Substring(0, max) : texto;
    }

    /// <summary>
    /// NestoAPI#614 (corte 4 de #610): el espejo de los cupones en PrestaShop, llamado directamente desde NestoAPI (decisión de
    /// Carlos, 08/10/26: no vía nesto_sync). Se reconcilia el estado: lo que tiene que haber en la tienda sale del cupón
    /// (<see cref="ReglasEspejoPrestashop.EstadoDeseado"/>) y lo que hay, de Cupones.EstadoEspejo. Así una llamada que falla, un
    /// pedido que se deshace o un cupón que vuelve a Activo se arreglan solos en la siguiente pasada del job (cada 15 minutos),
    /// como los reintentos de las agencias. Si lleva más de Cupones:HorasAvisoEspejo horas sin poder llevarse, un correo (una vez).
    /// </summary>
    public class ServicioEspejoCuponesPrestashop : IEspejoCupones
    {
        public const int MAXIMO_POR_PASADA = 50;
        /// <summary>Dentro de la transacción del pedido: poco, para no tener el pedido esperando a la tienda.</summary>
        public static readonly TimeSpan TIEMPO_ANTES_DE_CONFIRMAR = TimeSpan.FromSeconds(5);

        private readonly IRepositorioEspejoCupones repositorio;
        private readonly IClienteWebservicePrestashop tienda;
        private readonly IBuscadorCuentaPrestashop cuentas;
        private readonly Func<bool> encendido;
        private readonly Func<DateTime> ahora;
        private readonly Func<IServicioCorreoElectronico> correo;
        private readonly int idIdioma;
        private readonly int idMoneda;

        public ServicioEspejoCuponesPrestashop(NVEntities db, TimeSpan? tiempoMaximo = null)
            : this(new RepositorioEspejoCupones(db), new ClienteWebservicePrestashop(tiempoMaximo), () => ReglasEspejoPrestashop.Encendido, null,
                  () => new ServicioCorreoElectronico(), ReglasEspejoPrestashop.IdIdioma, ReglasEspejoPrestashop.IdMoneda)
        {
        }

        internal ServicioEspejoCuponesPrestashop(IRepositorioEspejoCupones repositorio, IClienteWebservicePrestashop tienda, Func<bool> encendido,
            Func<DateTime> ahora, Func<IServicioCorreoElectronico> correo, int idIdioma = 1, int idMoneda = 1,
            IBuscadorCuentaPrestashop cuentas = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.tienda = tienda ?? throw new ArgumentNullException(nameof(tienda));
            // NestoAPI#614 (Carlos, 22/10/26): el mismo casamiento cliente ↔ tienda que CanalesExternos (por el NIF de las direcciones)
            this.cuentas = cuentas ?? new BuscadorCuentaPrestashop(tienda);
            this.encendido = encendido ?? (() => false);
            this.ahora = ahora ?? (() => DateTime.Now);
            this.correo = correo;
            this.idIdioma = idIdioma;
            this.idMoneda = idMoneda;
        }

        public bool Encendido => encendido();

        public async Task Sincronizar(int cuponId)
        {
            if (!encendido())
            {
                return;
            }
            try
            {
                CuponEspejo cupon = await repositorio.Leer(cuponId).ConfigureAwait(false);
                if (cupon == null || !ReglasEspejoPrestashop.HayQueSincronizar(cupon, ahora()))
                {
                    return;
                }
                await Guardar(cupon, await Espejar(cupon).ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #614] No se ha podido llevar a PrestaShop el cupón {cuponId}: {ex.Message}. Lo reintenta el job.", ex));
            }
        }

        public async Task<ResultadoEspejo> DesactivarAntesDeConfirmar(int cuponId)
        {
            if (!encendido())
            {
                return null;
            }
            CuponEspejo cupon;
            try
            {
                cupon = await repositorio.Leer(cuponId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #614] No se ha podido leer el espejo del cupón {cuponId} antes de confirmar el pedido: {ex.Message}", ex));
                return null;
            }
            if (cupon == null || cupon.EstadoEspejo?.Trim() != EstadosEspejo.ACTIVO || cupon.IdCartRulePrestashop == null)
            {
                return null; // no está activo en la tienda: nada que quitar
            }
            try
            {
                await Desactivar(cupon).ConfigureAwait(false);
                return new ResultadoEspejo
                {
                    CuponId = cupon.Id,
                    Correcto = true,
                    EstadoEspejo = EstadosEspejo.INACTIVO,
                    IdCartRule = cupon.IdCartRulePrestashop,
                    IdCustomer = cupon.IdCustomerPrestashop
                };
            }
            catch (Exception ex)
            {
                // El pedido sigue: PrestaShop no puede parar las ventas. Lo desactiva el job; si a la vez lo gastan en la tienda,
                // entra por POST api/Cupones/{codigo}/Usar y se avisa a Administración.
                return new ResultadoEspejo
                {
                    CuponId = cupon.Id,
                    Correcto = false,
                    EstadoEspejo = cupon.EstadoEspejo?.Trim(),
                    Error = $"No se ha podido desactivar antes de confirmar el pedido: {ex.Message}"
                };
            }
        }

        public async Task ApuntarResultado(ResultadoEspejo resultado)
        {
            if (resultado == null)
            {
                return;
            }
            try
            {
                await repositorio.Guardar(resultado).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #614] No se ha podido apuntar el espejo del cupón {resultado.CuponId}: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// El job: lleva a PrestaShop lo pendiente (hasta <see cref="MAXIMO_POR_PASADA"/>) y avisa, una vez, de los que llevan más de
        /// <paramref name="horasAviso"/> horas sin poder llevarse. Devuelve cuántos han quedado bien.
        /// </summary>
        public async Task<int> ProcesarPendientes(int horasAviso)
        {
            if (!encendido())
            {
                return 0;
            }
            DateTime momento = ahora();
            int bien = 0;
            foreach (CuponEspejo cupon in await repositorio.LeerPendientes(MAXIMO_POR_PASADA).ConfigureAwait(false) ?? new List<CuponEspejo>())
            {
                if (!ReglasEspejoPrestashop.HayQueSincronizar(cupon, momento))
                {
                    continue;
                }
                ResultadoEspejo resultado = await Espejar(cupon).ConfigureAwait(false);
                await Guardar(cupon, resultado).ConfigureAwait(false);
                if (resultado.Correcto)
                {
                    bien++;
                }
            }
            await AvisarRetrasados(horasAviso).ConfigureAwait(false);
            return bien;
        }

        private async Task Guardar(CuponEspejo cupon, ResultadoEspejo resultado)
        {
            if (!resultado.Correcto && cupon.EspejoIntentos == 0)
            {
                // Solo el primer fallo a ELMAH: los siguientes, en EspejoError, y el correo si pasa de las horas de aviso
                ElmahHelper.Log(new Exception($"[Cupones #614] No se ha podido llevar a PrestaShop el cupón {cupon.Codigo?.Trim()}: {resultado.Error}"));
            }
            await repositorio.Guardar(resultado).ConfigureAwait(false);
        }

        /// <summary>Crea, reactiva o desactiva el cart_rule. No lanza: el fallo va en el resultado.</summary>
        internal async Task<ResultadoEspejo> Espejar(CuponEspejo cupon)
        {
            var resultado = new ResultadoEspejo { CuponId = cupon.Id, IdCartRule = cupon.IdCartRulePrestashop, IdCustomer = cupon.IdCustomerPrestashop };
            try
            {
                if (ReglasEspejoPrestashop.EstadoDeseado(cupon, ahora()) == EstadosEspejo.INACTIVO)
                {
                    if (cupon.IdCartRulePrestashop.HasValue)
                    {
                        await Desactivar(cupon).ConfigureAwait(false);
                    }
                    resultado.Correcto = true;
                    resultado.EstadoEspejo = EstadosEspejo.INACTIVO;
                    return resultado;
                }

                int? idCustomer = cupon.IdCustomerPrestashop ?? await BuscarCustomer(cupon).ConfigureAwait(false);
                if (idCustomer == null)
                {
                    resultado.Correcto = true;
                    resultado.EstadoEspejo = EstadosEspejo.SIN_CLIENTE;
                    resultado.Error = $"El cliente {cupon.Cliente?.Trim()} no tiene cuenta en la tienda (ninguna dirección con su NIF casa con él, " +
                        "como al importar los pedidos): el cupón solo vale en Nesto, NestoApp y la app de la tienda. Se vuelve a mirar cada día.";
                    return resultado;
                }
                resultado.IdCustomer = idCustomer;

                // Si un POST anterior llegó a la tienda y no se apuntó, el código ya está allí: se reutiliza (nunca dos con el mismo código)
                int? idCartRule = cupon.IdCartRulePrestashop
                    ?? ReglasEspejoPrestashop.LeerId(await tienda.Leer($"cart_rules?{ReglasEspejoPrestashop.Filtro("code", cupon.Codigo?.Trim())}&display=[id]")
                        .ConfigureAwait(false), "cart_rule");
                if (idCartRule == null)
                {
                    string creado = await tienda.Crear("cart_rules",
                        ReglasEspejoPrestashop.XmlCartRule(cupon, idCustomer.Value, true, null, idIdioma, idMoneda)).ConfigureAwait(false);
                    idCartRule = ReglasEspejoPrestashop.LeerId(creado, "cart_rule")
                        ?? throw new PrestashopWebserviceException("La tienda no ha devuelto el id del cart_rule creado.");
                }
                else
                {
                    _ = await tienda.Modificar($"cart_rules/{idCartRule.Value}",
                        ReglasEspejoPrestashop.XmlCartRule(cupon, idCustomer.Value, true, idCartRule, idIdioma, idMoneda)).ConfigureAwait(false);
                }
                resultado.IdCartRule = idCartRule;
                resultado.Correcto = true;
                resultado.EstadoEspejo = EstadosEspejo.ACTIVO;
                return resultado;
            }
            catch (Exception ex)
            {
                resultado.Correcto = false;
                resultado.EstadoEspejo = cupon.EstadoEspejo?.Trim();
                resultado.Error = ex.Message;
                return resultado;
            }
        }

        private Task Desactivar(CuponEspejo cupon) =>
            tienda.Modificar($"cart_rules/{cupon.IdCartRulePrestashop.Value}",
                ReglasEspejoPrestashop.XmlCartRule(cupon, cupon.IdCustomerPrestashop ?? 0, false, cupon.IdCartRulePrestashop, idIdioma, idMoneda));

        /// <summary>
        /// La cuenta de la tienda del cliente con el MISMO casamiento que CanalesExternos al importar los pedidos (NIF de las
        /// direcciones, CasamientoClientePrestashop); con varias, la más reciente. Null si no tiene ninguna.
        /// </summary>
        internal async Task<int?> BuscarCustomer(CuponEspejo cupon)
        {
            string nif = await repositorio.LeerNifCliente(cupon.Empresa, cupon.Cliente).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(nif) ? null : await cuentas.CuentaDelCliente(cupon.Cliente, nif).ConfigureAwait(false);
        }

        private async Task AvisarRetrasados(int horas)
        {
            List<CuponEspejo> retrasados;
            try
            {
                retrasados = await repositorio.LeerRetrasados(horas).ConfigureAwait(false) ?? new List<CuponEspejo>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #614] No se han podido leer los cupones retrasados en PrestaShop: {ex.Message}", ex));
                return;
            }
            if (!retrasados.Any() || correo == null)
            {
                return;
            }
            try
            {
                using (MailMessage mail = CorreoRetrasados(retrasados, horas))
                {
                    if (correo().EnviarCorreoSMTP(mail))
                    {
                        await repositorio.MarcarAvisados(retrasados.Select(c => c.Id)).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #614] No se ha podido avisar de los cupones retrasados en PrestaShop: {ex.Message}", ex));
            }
        }

        internal static MailMessage CorreoRetrasados(List<CuponEspejo> cupones, int horas)
        {
            string Html(string t) => WebUtility.HtmlEncode(t ?? string.Empty);
            var cuerpo = new StringBuilder();
            cuerpo.Append("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 14px;'>");
            cuerpo.Append($"<p>Estos cupones llevan más de {horas} horas sin poder llevarse a la tienda PrestaShop. En Nesto valen igual; " +
                "en la tienda, mientras tanto, el cliente no puede usarlos (o, si ya no valen, todavía se podrían usar allí). " +
                "NestoAPI lo sigue intentando cada 15 minutos.</p><ul>");
            foreach (CuponEspejo c in cupones)
            {
                cuerpo.Append($"<li><b>{Html(c.Codigo?.Trim())}</b> (cliente {Html(c.Cliente?.Trim())}, {ReglasCupon.Euros(c.Importe)} €, {Html(c.Estado?.Trim())}): " +
                    $"{Html(c.EspejoError)} ({c.EspejoIntentos} intentos)</li>");
            }
            cuerpo.Append("</ul><p>Si es un permiso del webservice (cart_rules o customers), hay que darlo en PrestaShop.</p></body></html>");
            var mail = new MailMessage
            {
                From = new MailAddress(Constantes.Correos.TIENDA_ONLINE, "NestoAPI"),
                Subject = $"Cupones sin llevar a la tienda PrestaShop ({cupones.Count})",
                Body = cuerpo.ToString(),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.INFORMATICA);
            mail.CC.Add(Constantes.Correos.TIENDA_ONLINE);
            return mail;
        }
    }
}
