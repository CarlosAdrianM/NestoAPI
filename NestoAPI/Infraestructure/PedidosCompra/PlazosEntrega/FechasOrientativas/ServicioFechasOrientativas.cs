using Hangfire;
using NestoAPI.Infraestructure.IndicadoresStock;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.FechasOrientativas
{
    /// <summary>NestoAPI#861: lo que ha hecho una pasada de <see cref="FechasOrientativasJobsService"/>.</summary>
    public class ResultadoFechasOrientativas
    {
        public bool Hecho { get; set; }
        public int PedidosVenta { get; set; }
        public int PedidosConError { get; set; }
        public int Productos { get; set; }
        public int Insertadas { get; set; }
        public int Primeras { get; set; }
        public int Abiertas { get; set; }
        public int ConPedido { get; set; }
        public int Resueltas { get; set; }
        public int Caducadas { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// NestoAPI#861: apunta cada día la fecha orientativa de lo no pedido (FOTO) y la compara con lo que pasó (RESOLVER), en
    /// dbo.FechasOrientativasSinPedir. Nunca en el camino caliente: lo lanza el job nocturno. La FOTO recorre los pedidos de venta pendientes
    /// con el MISMO motor de la fecha de entrega (sin avisos) para saber qué partidas se quedan sin fecha y con orientativa de lo no pedido, y
    /// desglosa la regla con los plazos (caché) y el proveedor habitual. Cada paso es tolerante a fallos: uno no impide el otro.
    /// </summary>
    public class ServicioFechasOrientativas
    {
        public const string MENSAJE_SIN_TABLA = "Todavía no existe la tabla de las fechas orientativas: hay que lanzar Scripts/Issue861_FechasOrientativasSinPedir.sql (como sa).";
        public const int MESES_MAXIMO = 24;
        internal const int LOTE_RESOLVER = 500;
        internal const int LOTE_PROVEEDORES = 500;
        private const string PREFIJO = "[Fechas orientativas #861]";

        private readonly IRepositorioFechasOrientativas repositorio;
        private readonly Func<string, int, Task<FechaEntregaAgenciaDTO>> calcular;
        private readonly Func<string, PlazosProveedores> plazos;
        private readonly Func<string, IEnumerable<string>, Dictionary<string, string>> proveedorHabitual;
        private readonly Func<DateTime> ahora;
        private readonly Func<DateTime, bool> esFestivo;
        private readonly Action<Exception> registrar;

        public ServicioFechasOrientativas(IRepositorioFechasOrientativas repositorio, Func<string, int, Task<FechaEntregaAgenciaDTO>> calcular,
            Func<string, PlazosProveedores> plazos, Func<string, IEnumerable<string>, Dictionary<string, string>> proveedorHabitual,
            Func<DateTime> ahora = null, Func<DateTime, bool> esFestivo = null, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.calcular = calcular;
            this.plazos = plazos;
            this.proveedorHabitual = proveedorHabitual;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.esFestivo = esFestivo ?? (d => GestorFestivos.EsFestivo(d, Constantes.Almacenes.ALGETE));
            this.registrar = registrar ?? (_ => { });
        }

        public bool TablaExiste() => repositorio.TablaExiste();

        /// <summary>Foto de hoy y resolución de las abiertas. Sin tabla, nada. Nunca lanza.</summary>
        public async Task<ResultadoFechasOrientativas> Procesar(string empresa)
        {
            var resultado = new ResultadoFechasOrientativas();
            try
            {
                if (!repositorio.TablaExiste())
                {
                    resultado.Mensaje = MENSAJE_SIN_TABLA;
                    return resultado;
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"{PREFIJO} No se ha podido comprobar la tabla: no se hace nada. {ex.Message}", ex));
                resultado.Mensaje = ex.Message;
                return resultado;
            }
            resultado.Hecho = true;
            string clave = CachePlazosProveedores.EmpresaPlazos(empresa);
            try
            {
                await Foto(clave, resultado).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"{PREFIJO} Error al apuntar la foto de hoy (se sigue con el resolver): {ex.Message}", ex));
            }
            try
            {
                Resolver(clave, resultado);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"{PREFIJO} Error al resolver las fotos abiertas: {ex.Message}", ex));
            }
            resultado.Mensaje = $"Foto: {resultado.Insertadas} productos apuntados ({resultado.Primeras} empiezan episodio) de {resultado.Productos} sin pedir " +
                $"en {resultado.PedidosVenta} pedidos de venta ({resultado.PedidosConError} con error). Resolver: {resultado.Abiertas} abiertas, " +
                $"{resultado.ConPedido} con pedido, {resultado.Resueltas} resueltas, {resultado.Caducadas} caducadas.";
            return resultado;
        }

        private async Task Foto(string empresa, ResultadoFechasOrientativas resultado)
        {
            DateTime hoy = ahora().Date;
            PlazosProveedores leidos = plazos?.Invoke(empresa);
            if (leidos == null || !leidos.TieneDatos || calcular == null)
            {
                return; // sin plazos no hay orientativas
            }
            List<int> pedidos = repositorio.LeerPedidosVentaPendientes(empresa) ?? new List<int>();
            var calculos = new List<KeyValuePair<int, FechaEntregaAgenciaDTO>>();
            Exception primerError = null;
            foreach (int pedido in pedidos.Distinct())
            {
                resultado.PedidosVenta++;
                try
                {
                    FechaEntregaAgenciaDTO calculo = await calcular(empresa, pedido).ConfigureAwait(false);
                    if (calculo != null)
                    {
                        calculos.Add(new KeyValuePair<int, FechaEntregaAgenciaDTO>(pedido, calculo));
                    }
                }
                catch (Exception ex)
                {
                    resultado.PedidosConError++;
                    primerError = primerError ?? new Exception($"{PREFIJO} Pedido {pedido}: {ex.Message}", ex);
                }
            }
            if (primerError != null)
            {
                registrar(new Exception($"{primerError.Message} ({resultado.PedidosConError} pedidos con error de {resultado.PedidosVenta})", primerError.InnerException));
            }

            List<ProductoSinPedir> productos = ReglasFechasOrientativas.ProductosSinPedir(calculos);
            resultado.Productos = productos.Count;
            if (!productos.Any())
            {
                return;
            }
            var proveedores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < productos.Count; i += LOTE_PROVEEDORES)
            {
                Dictionary<string, string> lote = proveedorHabitual?.Invoke(empresa, productos.Skip(i).Take(LOTE_PROVEEDORES).Select(p => p.Producto).ToList());
                foreach (KeyValuePair<string, string> par in lote ?? new Dictionary<string, string>())
                {
                    proveedores[par.Key.Trim()] = par.Value;
                }
            }
            Dictionary<string, DateTime> ultimas = repositorio.UltimasFotosAnteriores(empresa, hoy, hoy.AddDays(-ReglasFechasOrientativas.DIAS_BUSCAR_EPISODIO))
                ?? new Dictionary<string, DateTime>();
            foreach (ProductoSinPedir producto in productos)
            {
                _ = proveedores.TryGetValue(producto.Producto, out string proveedor);
                DateTime? ultima = ultimas.TryGetValue(producto.Producto, out DateTime dia) ? dia : (DateTime?)null;
                FotoFechaOrientativa foto = ReglasFechasOrientativas.ConstruirFoto(hoy, empresa, producto, proveedor, leidos, ultima, esFestivo);
                if (foto != null && repositorio.Insertar(foto))
                {
                    resultado.Insertadas++;
                    if (foto.PrimeraDelEpisodio)
                    {
                        resultado.Primeras++;
                    }
                }
            }
        }

        private void Resolver(string empresa, ResultadoFechasOrientativas resultado)
        {
            DateTime momento = ahora();
            List<int> ids = repositorio.IdsAbiertas(empresa) ?? new List<int>();
            for (int i = 0; i < ids.Count; i += LOTE_RESOLVER)
            {
                List<int> lote = ids.Skip(i).Take(LOTE_RESOLVER).ToList();
                List<FotoAbiertaConPedido> abiertas = repositorio.LeerAbiertas(empresa, lote.First(), lote.Last()) ?? new List<FotoAbiertaConPedido>();
                List<ResolucionFechaOrientativa> resoluciones = abiertas
                    .Where(a => a?.Foto != null)
                    .Select(a => ReglasFechasOrientativas.Resolver(a.Foto, a.Real, momento))
                    .ToList();
                resultado.Abiertas += resoluciones.Count;
                resultado.ConPedido += resoluciones.Count(r => r.PedidoReal.HasValue);
                resultado.Resueltas += resoluciones.Count(r => r.ResueltaEl.HasValue);
                resultado.Caducadas += resoluciones.Count(r => r.Caducada);
                _ = repositorio.Actualizar(resoluciones.Where(r => r.Cambia).ToList());
            }
        }

        /// <summary>El resumen de los últimos <paramref name="meses"/> (1 a <see cref="MESES_MAXIMO"/>).</summary>
        public ResumenFechasOrientativasDTO Resumen(string empresa, int meses)
        {
            string clave = CachePlazosProveedores.EmpresaPlazos(empresa);
            int mesesValidos = MesesValidos(meses);
            DateTime desde = ahora().Date.AddMonths(-mesesValidos);
            List<FilaErrorOrientativa> filas = repositorio.LeerResueltas(clave, desde) ?? new List<FilaErrorOrientativa>();
            (int abiertas, int caducadas) = repositorio.ContarPendientes(clave, desde);
            return ReglasFechasOrientativas.Resumir(filas, mesesValidos, desde, abiertas, caducadas);
        }

        internal static int MesesValidos(int meses) => Math.Max(1, Math.Min(MESES_MAXIMO, meses));
    }

    /// <summary>
    /// NestoAPI#861: job nocturno <see cref="JOB_ID"/> (todos los días a las 3:50 de Madrid, después del recálculo de plazos de las 3:40, en
    /// la misma cola de cálculos nocturnos <see cref="IndicadoresStockJobsService.COLA"/>) y el lanzamiento a mano (POST
    /// api/Compras/PlazosProveedores/Orientativas/Procesar). Sin la tabla no hace nada (ELMAH una vez). Nunca lanza.
    /// </summary>
    public static class FechasOrientativasJobsService
    {
        public const string JOB_ID = "fechas-orientativas-sin-pedir";
        public const string CRON = "50 3 * * *";
        private const string USUARIO_ELMAH = "Sistema (fechas orientativas)";

        private static bool sinTablaAvisado;

        [Queue(IndicadoresStockJobsService.COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static async Task Procesar()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    Action<Exception> registrar = ex => ElmahHelper.Log(ex, USUARIO_ELMAH);
                    RepositorioPlazosProveedores repositorioPlazos = RepositorioPlazosProveedores.DesdeConfiguracion();
                    // El mismo motor que ve el usuario, sin el aviso a Compras de proveedor vencido (nadie espera).
                    var fechas = new ServicioFechaEntregaAgencia(new RepositorioFechaEntregaAgencia(db), () => DateTime.Now, GestorFestivos.EsFestivo,
                        registrar, avisadorProveedorVencido: null, lectorCerteza: ServicioFechaEntregaAgencia.LectorCertezaPorDefecto());
                    var servicio = new ServicioFechasOrientativas(RepositorioFechasOrientativas.DesdeConfiguracion(),
                        (empresa, pedido) => fechas.CalcularPedido(empresa, pedido),
                        empresa => CachePlazosProveedores.Obtener(() => repositorioPlazos, empresa, registrar),
                        (empresa, productos) => repositorioPlazos.ProveedorHabitual(empresa, productos),
                        registrar: registrar);
                    ResultadoFechasOrientativas resultado = await Ejecutar(servicio, registrar).ConfigureAwait(false);
                    Console.WriteLine($"[Fechas orientativas #861] {resultado.Mensaje}");
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Fechas orientativas #861] Error en la pasada: {ex.Message}", ex), USUARIO_ELMAH);
            }
        }

        /// <summary>La pasada con el aviso «sin tabla» una sola vez (hasta que la tabla aparezca).</summary>
        internal static async Task<ResultadoFechasOrientativas> Ejecutar(ServicioFechasOrientativas servicio, Action<Exception> registrar)
        {
            ResultadoFechasOrientativas resultado = await servicio.Procesar(Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
            if (!resultado.Hecho && resultado.Mensaje == ServicioFechasOrientativas.MENSAJE_SIN_TABLA)
            {
                if (!sinTablaAvisado)
                {
                    sinTablaAvisado = true;
                    registrar(new Exception("[Fechas orientativas #861] " + ServicioFechasOrientativas.MENSAJE_SIN_TABLA + " No se apunta nada (se avisa una vez)."));
                }
            }
            else
            {
                sinTablaAvisado = false;
            }
            return resultado;
        }

        /// <summary>Solo para tests.</summary>
        internal static void OlvidarAvisos() => sinTablaAvisado = false;
    }
}
