using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#606: lo que hace falta de un pedido grabado para calcular su fecha de entrega a la agencia.</summary>
    public class PedidoFechaEntregaAgencia
    {
        public string Empresa { get; set; }
        public int Numero { get; set; }
        public string Ruta { get; set; }
        public byte? ModoServicio { get; set; }
        public bool ServirJunto { get; set; }
        public byte? ModoFacturacion { get; set; }
        public bool MantenerJunto { get; set; }
        public string DiasEnServir { get; set; }
        /// <summary>Alguna línea ya en albarán o factura.</summary>
        public bool TieneLineasServidas { get; set; }
        /// <summary>Las líneas de producto pendientes (-1 y 1), en el orden del picking.</summary>
        public List<LineaPedidoFechaEntregaAgencia> Lineas { get; set; } = new List<LineaPedidoFechaEntregaAgencia>();
    }

    /// <summary>NestoAPI#606: acceso a datos de <see cref="ServicioFechaEntregaAgencia"/> (sustituible en los tests).</summary>
    public interface IRepositorioFechaEntregaAgencia
    {
        /// <summary>Null si el pedido no existe.</summary>
        Task<PedidoFechaEntregaAgencia> LeerPedido(string empresa, int numero);
        Task<string> LeerDiasEnServir(string empresa, string cliente, string contacto);
        /// <summary>Stock de los productos, con los pendientes SIN las líneas de <paramref name="pedidoExcluir"/>.</summary>
        DatosSombraModoServicio LeerStock(string empresa, int? pedidoExcluir, IEnumerable<string> productos);
        Task<List<ReposicionCalendario>> LeerCalendario(string empresa);
        TimeSpan LeerHoraCorte(string empresa);
        /// <summary>Null si no hay, o si la columna aún no existe.</summary>
        Task<DateTime?> LeerPrometida(string empresa, int numero);
        /// <summary>Solo si estaba a NULL (se escribe una vez). True si la ha escrito.</summary>
        Task<bool> GuardarPrometida(string empresa, int numero, DateTime fecha);
    }

    public interface IServicioFechaEntregaAgencia
    {
        /// <summary>La del pedido grabado (detalle del pedido), con la prometida. Null si el pedido no existe.</summary>
        Task<FechaEntregaAgenciaDTO> CalcularPedido(string empresa, int numero);
        /// <summary>La de la plantilla, sin pedido creado (o modificándolo: sus líneas no cuentan como pendientes de otros).</summary>
        Task<FechaEntregaAgenciaDTO> CalcularPlantilla(PedidoVentaDTO pedido);
        /// <summary>Al crear el pedido: calcula y guarda la prometida. NUNCA lanza (crear el pedido no puede fallar por esto).</summary>
        Task<DateTime?> GuardarPrometidaAlCrear(string empresa, int numero);
    }

    /// <summary>
    /// NestoAPI#606: orquesta <see cref="RepartidorStockFechaEntregaAgencia"/> y <see cref="CalculadoraFechaEntregaAgencia"/>
    /// con los datos de la BD (pedido, stock, calendario de reposiciones, hora de corte y días que cierra el cliente).
    /// </summary>
    public class ServicioFechaEntregaAgencia : IServicioFechaEntregaAgencia
    {
        private readonly IRepositorioFechaEntregaAgencia repositorio;
        private readonly Func<DateTime> reloj;
        private readonly Func<DateTime, string, bool> esFestivo;
        private readonly Action<Exception> registrar;

        public ServicioFechaEntregaAgencia(NVEntities db)
            : this(new RepositorioFechaEntregaAgencia(db), () => DateTime.Now, GestorFestivos.EsFestivo,
                  ex => ElmahHelper.Log(ex))
        {
        }

        internal ServicioFechaEntregaAgencia(IRepositorioFechaEntregaAgencia repositorio, Func<DateTime> reloj,
            Func<DateTime, string, bool> esFestivo, Action<Exception> registrar)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.reloj = reloj ?? (() => DateTime.Now);
            this.esFestivo = esFestivo ?? GestorFestivos.EsFestivo;
            this.registrar = registrar ?? (_ => { });
        }

        public async Task<FechaEntregaAgenciaDTO> CalcularPedido(string empresa, int numero)
        {
            string empresaLimpia = Empresa(empresa);
            PedidoFechaEntregaAgencia pedido = await repositorio.LeerPedido(empresaLimpia, numero).ConfigureAwait(false);
            if (pedido == null)
            {
                return null;
            }
            FechaEntregaAgenciaDTO dto = await Calcular(empresaLimpia, numero, pedido.Ruta,
                Constantes.Pedidos.ModosServicio.Efectivo(pedido.ModoServicio, pedido.ServirJunto),
                Constantes.Pedidos.ModosFacturacion.Efectivo(pedido.ModoFacturacion, pedido.MantenerJunto),
                pedido.DiasEnServir, pedido.TieneLineasServidas, pedido.Lineas).ConfigureAwait(false);
            dto.FechaPrometida = await repositorio.LeerPrometida(empresaLimpia, numero).ConfigureAwait(false);
            return dto;
        }

        public async Task<FechaEntregaAgenciaDTO> CalcularPlantilla(PedidoVentaDTO pedido)
        {
            if (pedido == null)
            {
                throw new ArgumentNullException(nameof(pedido));
            }
            string empresa = Empresa(pedido.empresa);
            string diasEnServir = string.IsNullOrWhiteSpace(pedido.cliente)
                ? null
                : await repositorio.LeerDiasEnServir(empresa, pedido.cliente.Trim(), pedido.contacto?.Trim()).ConfigureAwait(false);
            List<LineaPedidoFechaEntregaAgencia> lineas = (pedido.Lineas ?? new List<LineaPedidoVentaDTO>())
                .Where(l => l != null && (l.tipoLinea == null || l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                    && l.estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .Select(l => new LineaPedidoFechaEntregaAgencia
                {
                    Producto = l.Producto,
                    Almacen = string.IsNullOrWhiteSpace(l.almacen) ? Constantes.Almacenes.ALGETE : l.almacen,
                    Cantidad = l.Cantidad - l.recoger,
                    BaseImponible = l.BaseImponible,
                    FechaEntrega = l.fechaEntrega > DateTime.MinValue ? l.fechaEntrega : (DateTime?)null,
                    YaEnPicking = l.picking > 0
                })
                .ToList();
            bool tieneLineasServidas = (pedido.Lineas ?? new List<LineaPedidoVentaDTO>())
                .Any(l => l != null && l.estado >= Constantes.EstadosLineaVenta.ALBARAN);
            // Como ModosServicio/ModosFacturacion.Normalizar al grabar: el modo informado manda; sin él, el bit.
            byte modoServicio = pedido.modoServicio.HasValue && Constantes.Pedidos.ModosServicio.EsValido(pedido.modoServicio.Value)
                ? pedido.modoServicio.Value
                : Constantes.Pedidos.ModosServicio.Efectivo(null, pedido.servirJunto);
            byte modoFacturacion = pedido.modoFacturacion.HasValue && Constantes.Pedidos.ModosFacturacion.EsValido(pedido.modoFacturacion.Value)
                ? pedido.modoFacturacion.Value
                : Constantes.Pedidos.ModosFacturacion.Efectivo(null, pedido.mantenerJunto);
            return await Calcular(empresa, pedido.numero > 0 ? pedido.numero : (int?)null, pedido.ruta, modoServicio, modoFacturacion,
                diasEnServir, tieneLineasServidas, lineas).ConfigureAwait(false);
        }

        public async Task<DateTime?> GuardarPrometidaAlCrear(string empresa, int numero)
        {
            try
            {
                FechaEntregaAgenciaDTO dto = await CalcularPedido(empresa, numero).ConfigureAwait(false);
                DateTime? fecha = dto?.FechaEntregaAgencia;
                if (fecha.HasValue)
                {
                    _ = await repositorio.GuardarPrometida(Empresa(empresa), numero, fecha.Value.Date).ConfigureAwait(false);
                }
                return fecha;
            }
            catch (Exception ex)
            {
                try
                {
                    if (TocaRegistrar(reloj()))
                    {
                        registrar(new Exception($"NestoAPI#606: no se ha podido guardar la fecha de entrega a la agencia prometida del pedido {numero} " +
                            $"(se avisa como mucho una vez cada {INTERVALO_AVISOS.TotalMinutes:0} minutos): {ex.Message}", ex));
                    }
                }
                catch
                {
                    // Ni el registro puede romper la creación del pedido.
                }
                return null;
            }
        }

        /// <summary>Si la columna aún no existe, fallaría al crear CADA pedido: a ELMAH, como mucho una vez cada media hora.</summary>
        internal static readonly TimeSpan INTERVALO_AVISOS = TimeSpan.FromMinutes(30);
        private static readonly object cerrojoAvisos = new object();
        private static DateTime ultimoAviso = DateTime.MinValue;

        internal static bool TocaRegistrar(DateTime ahora)
        {
            lock (cerrojoAvisos)
            {
                if (ahora - ultimoAviso < INTERVALO_AVISOS && ahora >= ultimoAviso)
                {
                    return false;
                }
                ultimoAviso = ahora;
                return true;
            }
        }

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarAvisos()
        {
            lock (cerrojoAvisos)
            {
                ultimoAviso = DateTime.MinValue;
            }
        }

        private async Task<FechaEntregaAgenciaDTO> Calcular(string empresa, int? pedidoExcluir, string ruta, byte modoServicio, byte modoFacturacion,
            string diasEnServir, bool tieneLineasServidas, List<LineaPedidoFechaEntregaAgencia> lineas)
        {
            DateTime ahora = reloj();
            List<LineaPedidoFechaEntregaAgencia> deAlgete = (lineas ?? new List<LineaPedidoFechaEntregaAgencia>())
                .Where(l => l != null && l.Cantidad > 0 && RepartidorStockFechaEntregaAgencia.EsDeAlgete(l.Almacen))
                .ToList();
            List<string> productos = deAlgete.Select(l => l.Producto?.Trim()).Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            DatosSombraModoServicio datos = productos.Any()
                ? repositorio.LeerStock(empresa, pedidoExcluir, productos)
                : new DatosSombraModoServicio();
            List<ReposicionCalendario> calendario = await repositorio.LeerCalendario(empresa).ConfigureAwait(false) ?? new List<ReposicionCalendario>();
            TimeSpan horaCorte = repositorio.LeerHoraCorte(empresa);

            var calculadoraReposicion = new CalculadoraFechaReposicion(esFestivo);
            List<string> tiendasPorOrden = RepartidorStockFechaEntregaAgencia.TIENDAS
                .OrderBy(t => calculadoraReposicion.Calcular(calendario, t, Constantes.Almacenes.ALGETE, horaCorte, ahora, empresa)?.PedidoSaleEl ?? DateTime.MaxValue)
                .ToList();

            var entrada = new EntradaFechaEntregaAgencia
            {
                Empresa = empresa,
                Lineas = RepartidorStockFechaEntregaAgencia.Repartir(deAlgete, datos, tiendasPorOrden, ahora.Date),
                ModoServicio = modoServicio,
                ModoFacturacion = modoFacturacion,
                Ruta = ruta?.Trim(),
                DiasEnServir = diasEnServir,
                TieneLineasServidas = tieneLineasServidas,
                Ahora = ahora,
                HoraCorte = horaCorte,
                Calendario = calendario
            };
            ResultadoFechaEntregaAgencia resultado = new CalculadoraFechaEntregaAgencia(esFestivo).Calcular(entrada);
            return new FechaEntregaAgenciaDTO
            {
                PrimeraEntrega = resultado.PrimeraEntrega,
                EntregaCompleta = resultado.EntregaCompleta,
                FechaEntregaAgencia = resultado.FechaQueAplica,
                Aplica = resultado.AplicaCompleta ? FechaEntregaAgenciaDTO.APLICA_COMPLETA : FechaEntregaAgenciaDTO.APLICA_PRIMERA,
                Entregas = resultado.Entregas,
                Motivo = resultado.Motivo
            };
        }

        private static string Empresa(string empresa) =>
            string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }
}
