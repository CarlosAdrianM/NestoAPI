using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#593: una campaña de cheque regalo (fila de ChequesRegaloCampanas). Todo lo que cambia de una campaña a
    /// otra vive aquí, también lo que aún está por decidir (importe, mínimos, espera, fecha límite): se cambia en la
    /// tabla, no en el código.
    /// </summary>
    public class CampanaChequeRegalo
    {
        public string Codigo { get; set; }
        public string Empresa { get; set; }
        /// <summary>El producto ficticio de la campaña (CHEQUE50_OCT26): la línea negativa del canje.</summary>
        public string Producto { get; set; }
        public decimal ImporteBase { get; set; }
        /// <summary>Primer y último día (incluido) en que una factura genera cheque.</summary>
        public DateTime GeneraDesde { get; set; }
        public DateTime GeneraHasta { get; set; }
        /// <summary>Último día (incluido) para canjearlo.</summary>
        public DateTime CanjeHasta { get; set; }
        /// <summary>Base de producto mínima de la primera factura para que genere (0 = sin mínimo).</summary>
        public decimal MinimoFacturaQueGenera { get; set; }
        /// <summary>El pedido del canje tiene que SUPERAR esta base de producto (estrictamente mayor).</summary>
        public decimal MinimoCanje { get; set; }
        /// <summary>Días tras la entrega completa del pedido original para poder usarlo (0 = desde que se genera).</summary>
        public int DiasEsperaTrasEntrega { get; set; }
        public bool Activa { get; set; }
    }

    /// <summary>Una factura de un cliente, resumida para decidir si genera cheque.</summary>
    public class FacturaParaChequeRegalo
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
        public DateTime Fecha { get; set; }
        public string Serie { get; set; }
        public string Cliente { get; set; }
        /// <summary>Base imponible de las líneas de producto (TipoLinea 1) que no son ficticias.</summary>
        public decimal BaseProducto { get; set; }
        /// <summary>Base imponible de todas las líneas.</summary>
        public decimal BaseTotal { get; set; }
        /// <summary>Lleva alguna línea de producto (no ficticio) en negativo: es un cambio o una devolución.</summary>
        public bool TieneDevolucion { get; set; }
        /// <summary>
        /// Las líneas de producto (TipoLinea 1) con lo que hace falta para saber si cuentan para el cheque: lo mismo
        /// que el mínimo del canje (ReglasCanjeChequeRegalo.ComputaParaMinimo).
        /// </summary>
        public List<LineaFacturaParaChequeRegalo> LineasDeProducto { get; set; } = new List<LineaFacturaParaChequeRegalo>();
    }

    /// <summary>Una línea de producto de una factura (sumada por producto), para la base computable.</summary>
    public class LineaFacturaParaChequeRegalo
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
        public byte? TipoLinea { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Grupo { get; set; }
        public bool Ficticio { get; set; }
        public decimal BaseImponible { get; set; }
    }

    /// <summary>La cabecera de FacturaParaChequeRegalo tal como sale del SQL (SqlQuery: una columna por propiedad).</summary>
    internal class FilaFacturaParaChequeRegalo
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
        public DateTime Fecha { get; set; }
        public string Serie { get; set; }
        public string Cliente { get; set; }
        public decimal BaseProducto { get; set; }
        public decimal BaseTotal { get; set; }
        public bool TieneDevolucion { get; set; }
    }

    public class DecisionChequeRegalo
    {
        public bool Genera { get; set; }
        /// <summary>La factura que genera el cheque (la primera de venta normal de la ventana).</summary>
        public FacturaParaChequeRegalo Factura { get; set; }
        public string Motivo { get; set; }

        internal static DecisionChequeRegalo No(string motivo) => new DecisionChequeRegalo { Genera = false, Motivo = motivo };
    }

    public class ChequeRegaloNuevo
    {
        public string Campana { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string EmpresaFactura { get; set; }
        public string FacturaOrigen { get; set; }
        public DateTime FechaFactura { get; set; }
        public DateTime FechaGeneracion { get; set; }
        public DateTime? FechaActivacion { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>
    /// Las reglas de generación, sin base de datos: «Ya decidido» del tablero y de NestoAPI#593.
    /// </summary>
    public static class ReglasChequeRegalo
    {
        /// <summary>
        /// Los otros dos códigos de Amazon (AMAZON EU 29440 y VENTAS AMAZON 31482): no son de factura simplificada, así
        /// que no estaban en la lista de genéricos, y el 15/10/26 AMAZON EU recibió un cheque (el 31). Si se añade uno
        /// aquí, añadirlo también a SQL_CLIENTES_SIN_CHEQUE.
        /// </summary>
        internal static readonly string[] OTROS_CLIENTES_EXCLUIDOS = { "29440", "31482" };

        /// <summary>Clientes genéricos (público final, tienda online, pedido tienda, materiales cursos, Amazon).</summary>
        public static bool ClienteExcluido(string cliente)
            => string.IsNullOrWhiteSpace(cliente) || Constantes.ClientesEspeciales.EsClienteFacturaSimplificada(cliente)
               || OTROS_CLIENTES_EXCLUIDOS.Contains(cliente.Trim());

        /// <summary>
        /// Por qué una factura no es «de venta normal» (null si lo es): rectificativa, a cero o negativa, cambio o
        /// devolución, o sin producto (solo cuentas contables: portes, reembolso, cuotas, reparaciones). La serie GB
        /// es normal: genera y canjea (decisión de Carlos).
        /// </summary>
        public static string MotivoNoEsVentaNormal(FacturaParaChequeRegalo f)
        {
            if (RegistroSeriesVerifactu.EsSerieRectificativa(f.Serie?.Trim()))
            {
                return "es una rectificativa";
            }
            if (f.BaseTotal <= 0)
            {
                return "es a cero o negativa";
            }
            if (f.TieneDevolucion)
            {
                return "lleva devoluciones (cambio)";
            }
            if (f.BaseProducto <= 0)
            {
                return "no lleva producto";
            }
            return null;
        }

        internal const string MOTIVO_SIN_PRODUCTO_COMPUTABLE =
            "no lleva producto que cuente para el cheque (solo peluquería o packs de Navidad)";

        /// <summary>
        /// Base imponible de las líneas de la factura que cuentan para el cheque, con la misma regla que el mínimo del
        /// canje: sin cuentas contables, ficticios, nombres que empiezan por los prefijos de la campaña («PACK 26») ni
        /// grupos excluidos («PEL»).
        /// </summary>
        public static decimal BaseComputable(FacturaParaChequeRegalo f, CampanaCanjeChequeRegalo campana)
            => (f.LineasDeProducto ?? new List<LineaFacturaParaChequeRegalo>())
                .Where(l => ReglasCanjeChequeRegalo.ComputaParaMinimo(l.TipoLinea, new ProductoParaChequeRegalo
                {
                    Numero = l.Producto,
                    Nombre = l.Nombre,
                    Grupo = l.Grupo,
                    Ficticio = l.Ficticio
                }, campana))
                .Sum(l => l.BaseImponible);

        /// <summary>
        /// Por qué esta factura no genera el cheque (null si lo genera): no es de venta normal o no lleva nada que
        /// cuente para el mínimo del canje (un cliente que solo compra peluquería no podría gastarlo). Como con las
        /// rectificativas, esa factura no cuenta y lo puede generar una posterior del cliente dentro de la ventana.
        /// </summary>
        public static string MotivoNoGenera(FacturaParaChequeRegalo f, CampanaCanjeChequeRegalo campana)
            => MotivoNoEsVentaNormal(f) ?? (BaseComputable(f, campana) <= 0 ? MOTIVO_SIN_PRODUCTO_COMPUTABLE : null);

        public static bool DentroDeLaVentana(CampanaChequeRegalo campana, DateTime fecha)
            => fecha.Date >= campana.GeneraDesde.Date && fecha.Date <= campana.GeneraHasta.Date;

        /// <summary>
        /// La primera factura de venta normal del cliente dentro de la ventana es la que decide: si no llega al mínimo,
        /// ese cliente no genera (no se busca otra que llegue). Las que no llevan nada que cuente para el cheque (solo
        /// peluquería o packs de Navidad) no cuentan: decide la siguiente.
        /// </summary>
        public static DecisionChequeRegalo Decidir(CampanaCanjeChequeRegalo campana, string cliente, bool clienteFinDeMes,
            IEnumerable<FacturaParaChequeRegalo> facturasDelCliente, bool clienteDistribuidor = false)
        {
            if (campana == null || !campana.Activa)
            {
                return DecisionChequeRegalo.No("la campaña no está activa");
            }
            if (ClienteExcluido(cliente))
            {
                return DecisionChequeRegalo.No($"el cliente {cliente?.Trim()} es genérico");
            }
            if (clienteFinDeMes)
            {
                return DecisionChequeRegalo.No($"el cliente {cliente.Trim()} factura a fin de mes");
            }
            // 14/10/26 (Carlos, cliente 35295 el 13/10): los distribuidores no generan el cheque
            if (clienteDistribuidor)
            {
                return DecisionChequeRegalo.No($"el cliente {cliente.Trim()} es distribuidor");
            }

            FacturaParaChequeRegalo primera = (facturasDelCliente ?? Enumerable.Empty<FacturaParaChequeRegalo>())
                .Where(f => DentroDeLaVentana(campana, f.Fecha))
                .OrderBy(f => f.Fecha.Date)
                .ThenBy(f => f.Numero, StringComparer.Ordinal)
                .FirstOrDefault(f => MotivoNoGenera(f, campana) == null);
            if (primera == null)
            {
                return DecisionChequeRegalo.No("no tiene ninguna factura de venta normal con producto que cuente para el cheque en la ventana de la campaña");
            }
            if (primera.BaseProducto < campana.MinimoFacturaQueGenera)
            {
                return DecisionChequeRegalo.No($"su primera factura ({primera.Numero.Trim()}) no llega al mínimo de " +
                    $"{campana.MinimoFacturaQueGenera.ToString("0.00", CultureInfo.GetCultureInfo("es-ES"))} €");
            }
            return new DecisionChequeRegalo { Genera = true, Factura = primera };
        }

        /// <summary>
        /// Sin espera, el cheque se puede usar desde que se genera. Con espera, queda sin fecha hasta que el pedido
        /// original esté entregado entero y pasen los días (lo pone el canje, NestoAPI#593 c4).
        /// </summary>
        public static DateTime? FechaActivacion(CampanaChequeRegalo campana, DateTime fechaGeneracion)
            => campana.DiasEsperaTrasEntrega <= 0 ? fechaGeneracion : (DateTime?)null;

        public static string TextoAviso(CampanaChequeRegalo campana, string cliente)
        {
            CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
            return $"El cliente {cliente?.Trim()} ha recibido un cheque regalo de {campana.ImporteBase.ToString("0.##", es)} € + IVA " +
                $"para su próximo pedido de más de {campana.MinimoCanje.ToString("0.##", es)} € de producto computable, " +
                $"hasta el {campana.CanjeHasta.ToString("dd/MM/yyyy", es)}" +
                (campana.DiasEsperaTrasEntrega > 0
                    ? $" (se puede usar {campana.DiasEsperaTrasEntrega} días después de entregarle entero este pedido)."
                    : ".");
        }
    }

    public interface IRepositorioChequesRegalo
    {
        /// <summary>Con las exclusiones del mínimo del canje: también deciden qué factura genera.</summary>
        Task<List<CampanaCanjeChequeRegalo>> LeerCampanasActivas();
        /// <summary>Facturas del cliente (empresa normal y espejo) con fecha entre los dos días, ambos incluidos, con
        /// sus líneas de producto.</summary>
        Task<List<FacturaParaChequeRegalo>> LeerFacturasDelCliente(string cliente, DateTime desde, DateTime hasta);
        /// <summary>Algún contacto del cliente factura a fin de mes (el control es por código de cliente).</summary>
        Task<bool> EsClienteFinDeMes(string cliente);
        /// <summary>Algún contacto del cliente es distribuidor (estado 6) o contacto de distribuidor (estado 67).</summary>
        Task<bool> EsDistribuidor(string cliente);
        Task<bool> TieneCheque(string campana, string empresa, string cliente);
        /// <summary>False si ya había uno (la UNIQUE manda: lo generó otra factura a la vez).</summary>
        Task<bool> InsertarCheque(ChequeRegaloNuevo cheque);
        /// <summary>Clientes no genéricos con facturas en la ventana y sin cheque de esta campaña.</summary>
        Task<List<string>> LeerClientesSinCheque(CampanaChequeRegalo campana);
    }

    public interface IGeneradorChequesRegalo
    {
        /// <summary>Los avisos para quien factura (vacío si esta factura no genera cheque).</summary>
        Task<List<string>> GenerarPorFactura(string empresaFactura, string numeroFactura, string cliente, DateTime fechaFactura, string usuario);
        /// <summary>Genera los que falten (facturas hechas fuera de la API). Devuelve cuántos ha generado.</summary>
        Task<int> Reconciliar(DateTime hoy, string usuario);
    }

    /// <summary>
    /// NestoAPI#593 (c2): genera los cheques. Lo llama ServicioFacturas.CrearFactura (el único call site de
    /// prdCrearFacturaVta) tras crear la factura, y el job diario de reconciliación para lo facturado fuera de la
    /// API. Detrás del interruptor ChequesRegalo:Generar (apagado salvo "true") y de la campaña Activa.
    /// </summary>
    public class GeneradorChequesRegalo : IGeneradorChequesRegalo
    {
        /// <summary>Días después del fin de la ventana en que la reconciliación aún mira (facturas tardías fuera de la API).</summary>
        internal const int DIAS_RECONCILIACION_TRAS_VENTANA = 7;

        private readonly IRepositorioChequesRegalo repositorio;
        private readonly Func<bool> habilitado;

        public GeneradorChequesRegalo(IRepositorioChequesRegalo repositorio, Func<bool> habilitado = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.habilitado = habilitado ?? (() => InterruptorEncendido);
        }

        /// <summary>Web.config ChequesRegalo:Generar. Apagado si no está o no vale "true".</summary>
        public static bool InterruptorEncendido => string.Equals(
            ConfigurationManager.AppSettings["ChequesRegalo:Generar"], "true", StringComparison.OrdinalIgnoreCase);

        public async Task<List<string>> GenerarPorFactura(string empresaFactura, string numeroFactura, string cliente, DateTime fechaFactura, string usuario)
        {
            var avisos = new List<string>();
            if (!habilitado() || ReglasChequeRegalo.ClienteExcluido(cliente))
            {
                return avisos;
            }
            foreach (CampanaCanjeChequeRegalo campana in (await repositorio.LeerCampanasActivas().ConfigureAwait(false))
                .Where(c => ReglasChequeRegalo.DentroDeLaVentana(c, fechaFactura)))
            {
                ChequeRegaloNuevo generado = await GenerarSiToca(campana, cliente.Trim(), usuario).ConfigureAwait(false);
                if (generado != null && generado.EmpresaFactura?.Trim() == empresaFactura?.Trim()
                    && generado.FacturaOrigen?.Trim() == numeroFactura?.Trim())
                {
                    avisos.Add(ReglasChequeRegalo.TextoAviso(campana, cliente));
                }
            }
            return avisos;
        }

        public async Task<int> Reconciliar(DateTime hoy, string usuario)
        {
            if (!habilitado())
            {
                return 0;
            }
            int generados = 0;
            foreach (CampanaCanjeChequeRegalo campana in (await repositorio.LeerCampanasActivas().ConfigureAwait(false))
                .Where(c => hoy.Date >= c.GeneraDesde.Date && hoy.Date <= c.GeneraHasta.Date.AddDays(DIAS_RECONCILIACION_TRAS_VENTANA)))
            {
                foreach (string cliente in await repositorio.LeerClientesSinCheque(campana).ConfigureAwait(false))
                {
                    if (!ReglasChequeRegalo.ClienteExcluido(cliente)
                        && await GenerarSiToca(campana, cliente.Trim(), usuario).ConfigureAwait(false) != null)
                    {
                        generados++;
                    }
                }
            }
            return generados;
        }

        /// <summary>El cheque generado ahora, o null si no tocaba o ya lo tenía.</summary>
        private async Task<ChequeRegaloNuevo> GenerarSiToca(CampanaCanjeChequeRegalo campana, string cliente, string usuario)
        {
            if (await repositorio.TieneCheque(campana.Codigo, campana.Empresa, cliente).ConfigureAwait(false))
            {
                return null;
            }
            List<FacturaParaChequeRegalo> facturas = await repositorio
                .LeerFacturasDelCliente(cliente, campana.GeneraDesde.Date, campana.GeneraHasta.Date).ConfigureAwait(false);
            bool finDeMes = await repositorio.EsClienteFinDeMes(cliente).ConfigureAwait(false);
            bool distribuidor = await repositorio.EsDistribuidor(cliente).ConfigureAwait(false);
            DecisionChequeRegalo decision = ReglasChequeRegalo.Decidir(campana, cliente, finDeMes, facturas, distribuidor);
            if (!decision.Genera)
            {
                return null;
            }
            DateTime ahora = DateTime.Now;
            var cheque = new ChequeRegaloNuevo
            {
                Campana = campana.Codigo,
                Empresa = campana.Empresa,
                Cliente = cliente,
                EmpresaFactura = decision.Factura.Empresa?.Trim(),
                FacturaOrigen = decision.Factura.Numero?.Trim(),
                FechaFactura = decision.Factura.Fecha,
                FechaGeneracion = ahora,
                FechaActivacion = ReglasChequeRegalo.FechaActivacion(campana, ahora),
                Usuario = usuario
            };
            return await repositorio.InsertarCheque(cheque).ConfigureAwait(false) ? cheque : null;
        }
    }

    /// <summary>NestoAPI#593: las tablas del cheque regalo por SQL directo (sin EDMX), como PreparacionAlmacen.</summary>
    public class RepositorioChequesRegalo : IRepositorioChequesRegalo
    {
        private readonly NVEntities db;

        public RepositorioChequesRegalo(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        // Empresa normal y espejo (la serie GB se factura en la espejo). Productos.Ficticio por empresa de la línea.
        internal const string SQL_FACTURAS_DEL_CLIENTE = @"
SELECT RTRIM(f.Empresa) AS Empresa, RTRIM(f.[Número]) AS Numero, f.Fecha, RTRIM(f.Serie) AS Serie, RTRIM(f.[Nº Cliente]) AS Cliente,
       CAST(ISNULL(SUM(CASE WHEN l.TipoLinea = 1 AND ISNULL(p.Ficticio, 0) = 0 THEN l.[Base Imponible] ELSE 0 END), 0) AS decimal(18,4)) AS BaseProducto,
       CAST(ISNULL(SUM(l.[Base Imponible]), 0) AS decimal(18,4)) AS BaseTotal,
       CAST(MAX(CASE WHEN l.TipoLinea = 1 AND ISNULL(p.Ficticio, 0) = 0 AND l.Cantidad < 0 THEN 1 ELSE 0 END) AS bit) AS TieneDevolucion
FROM CabFacturaVta f
     LEFT JOIN LinPedidoVta l ON l.Empresa = f.Empresa AND l.[Nº Factura] = f.[Número]
     LEFT JOIN Productos p ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE f.Empresa IN (@p0, @p1) AND f.[Nº Cliente] = @p2 AND f.Fecha >= @p3 AND f.Fecha < @p4
GROUP BY f.Empresa, f.[Número], f.Fecha, f.Serie, f.[Nº Cliente]";

        // Las líneas de producto de esas facturas, sumadas por producto, con nombre, grupo y ficticio de la ficha: lo
        // que mira ReglasCanjeChequeRegalo.ComputaParaMinimo para saber si cuentan para el cheque.
        internal const string SQL_LINEAS_DE_PRODUCTO_DEL_CLIENTE = @"
SELECT RTRIM(f.Empresa) AS Empresa, RTRIM(f.[Número]) AS Numero, l.TipoLinea, RTRIM(l.Producto) AS Producto,
       RTRIM(p.Nombre) AS Nombre, RTRIM(p.Grupo) AS Grupo, CAST(ISNULL(p.Ficticio, 0) AS bit) AS Ficticio,
       CAST(SUM(l.[Base Imponible]) AS decimal(18,4)) AS BaseImponible
FROM CabFacturaVta f WITH (NOLOCK)
     INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = f.Empresa AND l.[Nº Factura] = f.[Número]
     LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE f.Empresa IN (@p0, @p1) AND f.[Nº Cliente] = @p2 AND f.Fecha >= @p3 AND f.Fecha < @p4 AND l.TipoLinea = 1
GROUP BY f.Empresa, f.[Número], l.TipoLinea, l.Producto, p.Nombre, p.Grupo, p.Ficticio";

        internal const string SQL_CLIENTE_FIN_DE_MES = @"
SELECT COUNT(*) FROM Clientes WHERE Empresa = @p0 AND [Nº Cliente] = @p1 AND PeriodoFacturación = @p2";

        internal const string SQL_CLIENTE_DISTRIBUIDOR = @"
SELECT COUNT(*) FROM Clientes WHERE Empresa = @p0 AND [Nº Cliente] = @p1 AND Estado IN (@p2, @p3)";

        internal const string SQL_TIENE_CHEQUE = @"
SELECT COUNT(*) FROM dbo.ChequesRegalo WHERE Campana = @p0 AND Empresa = @p1 AND Cliente = @p2";

        // La UNIQUE (Campana, Empresa, Cliente) hace de candado: con el WHERE NOT EXISTS y la UNIQUE, dos facturas a
        // la vez no generan dos cheques (la segunda no inserta nada o choca con la clave, y se trata como «ya estaba»)
        internal const string SQL_INSERTAR_CHEQUE = @"
INSERT INTO dbo.ChequesRegalo (Campana, Empresa, Cliente, EmpresaFactura, FacturaOrigen, FechaFactura, FechaGeneracion, FechaActivacion, Estado, Usuario)
SELECT @p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, 'Generado', @p8
WHERE NOT EXISTS (SELECT 1 FROM dbo.ChequesRegalo WITH (UPDLOCK, HOLDLOCK) WHERE Campana = @p0 AND Empresa = @p1 AND Cliente = @p2)";

        internal const string SQL_CLIENTES_SIN_CHEQUE = @"
SELECT DISTINCT RTRIM(f.[Nº Cliente])
FROM CabFacturaVta f
WHERE f.Empresa IN (@p0, @p1) AND f.Fecha >= @p2 AND f.Fecha < @p3
      AND RTRIM(f.[Nº Cliente]) NOT IN ('10458', '31517', '9500', '31794', '32624', '29440', '31482')
      AND NOT EXISTS (SELECT 1 FROM dbo.ChequesRegalo c WHERE c.Campana = @p4 AND c.Empresa = @p5 AND c.Cliente = RTRIM(f.[Nº Cliente]))";

        // Las mismas campañas que el canje, con sus exclusiones (sin tablas no hay campañas: no se genera nada; sin las
        // columnas de c4, salen sin exclusiones)
        public async Task<List<CampanaCanjeChequeRegalo>> LeerCampanasActivas()
            => (await db.Database.SqlQuery<CampanaCanjeChequeRegalo>(RepositorioCanjeChequesRegalo.SQL_CAMPANAS)
                .ToListAsync().ConfigureAwait(false))
                .Where(c => c.Activa)
                .ToList();

        public async Task<List<FacturaParaChequeRegalo>> LeerFacturasDelCliente(string cliente, DateTime desde, DateTime hasta)
        {
            List<FilaFacturaParaChequeRegalo> cabeceras = await db.Database.SqlQuery<FilaFacturaParaChequeRegalo>(
                SQL_FACTURAS_DEL_CLIENTE, ParametrosFacturasDelCliente(cliente, desde, hasta)).ToListAsync().ConfigureAwait(false);
            List<LineaFacturaParaChequeRegalo> lineas = await db.Database.SqlQuery<LineaFacturaParaChequeRegalo>(
                SQL_LINEAS_DE_PRODUCTO_DEL_CLIENTE, ParametrosFacturasDelCliente(cliente, desde, hasta)).ToListAsync().ConfigureAwait(false);
            return Juntar(cabeceras, lineas);
        }

        // Cada consulta necesita sus propios SqlParameter (no se pueden reutilizar entre comandos)
        private static object[] ParametrosFacturasDelCliente(string cliente, DateTime desde, DateTime hasta) => new object[]
        {
            new SqlParameter("@p0", Constantes.Empresas.EMPRESA_POR_DEFECTO),
            new SqlParameter("@p1", Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO),
            new SqlParameter("@p2", cliente),
            new SqlParameter("@p3", desde.Date),
            new SqlParameter("@p4", hasta.Date.AddDays(1))
        };

        internal static List<FacturaParaChequeRegalo> Juntar(IEnumerable<FilaFacturaParaChequeRegalo> cabeceras,
            IEnumerable<LineaFacturaParaChequeRegalo> lineas)
        {
            ILookup<string, LineaFacturaParaChequeRegalo> porFactura = (lineas ?? Enumerable.Empty<LineaFacturaParaChequeRegalo>())
                .ToLookup(l => Clave(l.Empresa, l.Numero));
            return (cabeceras ?? Enumerable.Empty<FilaFacturaParaChequeRegalo>()).Select(c => new FacturaParaChequeRegalo
            {
                Empresa = c.Empresa,
                Numero = c.Numero,
                Fecha = c.Fecha,
                Serie = c.Serie,
                Cliente = c.Cliente,
                BaseProducto = c.BaseProducto,
                BaseTotal = c.BaseTotal,
                TieneDevolucion = c.TieneDevolucion,
                LineasDeProducto = porFactura[Clave(c.Empresa, c.Numero)].ToList()
            }).ToList();
        }

        private static string Clave(string empresa, string numero) => $"{empresa?.Trim()}|{numero?.Trim()}";

        public async Task<bool> EsClienteFinDeMes(string cliente)
            => await db.Database.SqlQuery<int>(SQL_CLIENTE_FIN_DE_MES,
                new SqlParameter("@p0", Constantes.Empresas.EMPRESA_POR_DEFECTO),
                new SqlParameter("@p1", cliente),
                new SqlParameter("@p2", Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES)).FirstAsync().ConfigureAwait(false) > 0;

        public async Task<bool> EsDistribuidor(string cliente)
            => await db.Database.SqlQuery<int>(SQL_CLIENTE_DISTRIBUIDOR,
                new SqlParameter("@p0", Constantes.Empresas.EMPRESA_POR_DEFECTO),
                new SqlParameter("@p1", cliente),
                new SqlParameter("@p2", Constantes.Clientes.Estados.DISTRIBUIDOR),
                new SqlParameter("@p3", Constantes.Clientes.Estados.CONTACTO_DE_DISTRIBUIDOR)).FirstAsync().ConfigureAwait(false) > 0;

        public async Task<bool> TieneCheque(string campana, string empresa, string cliente)
            => await db.Database.SqlQuery<int>(SQL_TIENE_CHEQUE,
                new SqlParameter("@p0", campana), new SqlParameter("@p1", empresa), new SqlParameter("@p2", cliente))
                .FirstAsync().ConfigureAwait(false) > 0;

        public async Task<bool> InsertarCheque(ChequeRegaloNuevo cheque)
        {
            try
            {
                int filas = await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR_CHEQUE,
                    new SqlParameter("@p0", cheque.Campana),
                    new SqlParameter("@p1", cheque.Empresa),
                    new SqlParameter("@p2", cheque.Cliente),
                    new SqlParameter("@p3", (object)cheque.EmpresaFactura ?? DBNull.Value),
                    new SqlParameter("@p4", (object)cheque.FacturaOrigen ?? DBNull.Value),
                    new SqlParameter("@p5", cheque.FechaFactura),
                    new SqlParameter("@p6", cheque.FechaGeneracion),
                    new SqlParameter("@p7", System.Data.SqlDbType.DateTime) { Value = (object)cheque.FechaActivacion ?? DBNull.Value },
                    new SqlParameter("@p8", (object)cheque.Usuario ?? "Sistema")).ConfigureAwait(false);
                return filas > 0;
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                return false; // otra factura lo generó a la vez
            }
        }

        public async Task<List<string>> LeerClientesSinCheque(CampanaChequeRegalo campana)
            => await db.Database.SqlQuery<string>(SQL_CLIENTES_SIN_CHEQUE,
                new SqlParameter("@p0", Constantes.Empresas.EMPRESA_POR_DEFECTO),
                new SqlParameter("@p1", Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO),
                new SqlParameter("@p2", campana.GeneraDesde.Date),
                new SqlParameter("@p3", campana.GeneraHasta.Date.AddDays(1)),
                new SqlParameter("@p4", campana.Codigo),
                new SqlParameter("@p5", campana.Empresa)).ToListAsync().ConfigureAwait(false);
    }

    /// <summary>NestoAPI#593: el job diario (Hangfire) que genera los cheques de las facturas hechas fuera de la API.</summary>
    public static class ChequesRegaloJobsService
    {
        internal const string USUARIO_RECONCILIACION = "Reconciliación cheques regalo";

        public static async Task Reconciliar()
        {
            if (!GeneradorChequesRegalo.InterruptorEncendido)
            {
                Console.WriteLine("⏸️ [Hangfire] Cheques regalo: interruptor ChequesRegalo:Generar apagado, no se hace nada");
                return;
            }
            try
            {
                using (var db = new NVEntities())
                {
                    int generados = await new GeneradorChequesRegalo(new RepositorioChequesRegalo(db))
                        .Reconciliar(DateTime.Today, USUARIO_RECONCILIACION).ConfigureAwait(false);
                    // NestoAPI#593 (aviso): el correo de los generados ahora y de los que se quedaron sin él
                    // (fallo del SMTP al facturar, o cliente al que le han puesto el correo después)
                    int avisados = await AvisadorChequesRegalo.Crear(db).AvisarPendientes().ConfigureAwait(false);
                    Console.WriteLine($"✅ [Hangfire] Cheques regalo: {generados} generados y {avisados} correos en la reconciliación");
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cheques regalo #593] Falló la reconciliación diaria: {ex.Message}", ex));
                throw;
            }
        }
    }
}
