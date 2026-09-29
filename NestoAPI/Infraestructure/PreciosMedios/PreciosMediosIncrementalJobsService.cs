using Hangfire;
using System;
using System.Collections.Generic;
using System.Configuration;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Tipo propio para el resumen informativo del incremental en ELMAH (no es un error: se reconoce por la columna Type).
    /// </summary>
    public class PreciosMediosIncrementalInfo : Exception
    {
        public PreciosMediosIncrementalInfo(string mensaje) : base(mensaje) { }
    }

    /// <summary>
    /// Issue #547, corte (c): los jobs de Hangfire del INCREMENTAL de precios medios.
    /// - Pasada nocturna (02:30): recalcula los productos con compras modificadas desde la última pasada y los que
    ///   tienen movimientos de stock con fecha pasada (riesgo 2). Resumen en ELMAH (info) por pasada.
    /// - Al facturar una compra (PedidosCompraController, tras el commit): un job por pedido que encola un job por
    ///   producto. La facturación no espera a nada: solo encola.
    /// El SP del domingo sigue haciendo la pasada completa como red: con paridad, no cambia ninguna fila.
    ///
    /// INTERRUPTOR: <c>PreciosMedios:EscribirIncremental</c> en Web.config. Por defecto APAGADO (falta la clave o no dice
    /// "true"): los jobs no leen ni escriben nada y la facturación no encola. Se enciende cuando la sombra (corte b)
    /// lleve dos domingos seguidos sin diferencias no esperadas. OJO: el valor del Web.config del repo es el que se
    /// publica (encendido a mano en el servidor, la siguiente publicación lo apagaría).
    /// </summary>
    public static class PreciosMediosIncrementalJobsService
    {
        internal const string CLAVE_INTERRUPTOR = "PreciosMedios:EscribirIncremental";
        internal const string USUARIO_ELMAH = "Sistema (incremental de precios medios)";

        /// <summary>La pasada nocturna se corta a las 3 horas (02:30 → 05:30): no competir con la mañana.</summary>
        internal static readonly TimeSpan LIMITE_PASADA = TimeSpan.FromHours(3);

        /// <summary>Encendido solo con "true" explícito: escribir en tres tablas tiene que ser una decisión, no un descuido.</summary>
        internal static bool EstaActivo(string valorConfigurado)
        {
            return bool.TryParse(valorConfigurado?.Trim(), out bool activo) && activo;
        }

        internal static bool EstaActivo()
        {
            return EstaActivo(ConfigurationManager.AppSettings[CLAVE_INTERRUPTOR]);
        }

        internal static ServicioIncrementalPreciosMedios CrearServicio()
        {
            return new ServicioIncrementalPreciosMedios(RepositorioEscrituraPreciosMediosSql.DesdeConfiguracion(),
                RepositorioPreciosMediosSql.DesdeConfiguracion());
        }

        #region Puntos de entrada de Hangfire

        /// <summary>Job recurrente (todos los días a las 02:30). No hace nada con el interruptor apagado.</summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 4 * 3600)]
        public static void ProcesarPasadaNocturna()
        {
            try
            {
                _ = ProcesarNocturna(EstaActivo(), CrearServicio, DateTime.Now,
                    (empresa, producto) => BackgroundJob.Enqueue(() => RecalcularProducto(empresa, producto)));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Precios medios #547] Error en la pasada nocturna del incremental: " + ex.Message, ex), USUARIO_ELMAH);
                // Sin relanzar: la marca no ha avanzado, la noche siguiente repasa lo mismo.
            }
        }

        /// <summary>Recalcula un producto (encolado al facturar o reintento de la pasada nocturna). Reintenta si falla.</summary>
        [AutomaticRetry(Attempts = 3)]
        public static void RecalcularProducto(string empresa, string producto)
        {
            _ = ProcesarProducto(EstaActivo(), CrearServicio, empresa, producto);
        }

        /// <summary>Tras facturar un pedido de compra: encola un <see cref="RecalcularProducto"/> por producto del pedido.</summary>
        [AutomaticRetry(Attempts = 3)]
        public static void RecalcularPedidoCompra(string empresa, int pedido)
        {
            _ = ProcesarPedido(EstaActivo(), CrearServicio, empresa, pedido,
                (e, p) => BackgroundJob.Enqueue(() => RecalcularProducto(e, p)));
        }

        /// <summary>
        /// Lo llama la facturación de compras DESPUÉS del commit. Nunca lanza: la factura ya está hecha y el SP del
        /// domingo (y la pasada nocturna) recogerán lo que aquí no se encole.
        /// </summary>
        public static void EncolarTrasFacturarCompra(string empresa, int pedido)
        {
            _ = Encolar(EstaActivo(), empresa, pedido, (e, p) => BackgroundJob.Enqueue(() => RecalcularPedidoCompra(e, p)));
        }

        #endregion

        #region Núcleos testables (dependencias inyectadas)

        /// <summary>Null si el interruptor está apagado: ni se crea el servicio ni se lee nada.</summary>
        internal static ResumenPasadaIncrementalPrecioMedio ProcesarNocturna(bool activo, Func<ServicioIncrementalPreciosMedios> crearServicio,
            DateTime ahora, Action<string, string> reintentar)
        {
            if (!activo)
            {
                return null;
            }
            ServicioIncrementalPreciosMedios servicio = crearServicio();
            ResumenPasadaIncrementalPrecioMedio resumen = servicio.EjecutarPasadaNocturna(ahora, LIMITE_PASADA, reintentar);
            string mensaje = "[Precios medios #547] " + resumen;
            if (resumen.Errores > 0)
            {
                ElmahHelper.Log(new Exception(mensaje), USUARIO_ELMAH);
            }
            else
            {
                ElmahHelper.Log(new PreciosMediosIncrementalInfo(mensaje), USUARIO_ELMAH);
            }
            return resumen;
        }

        /// <summary>
        /// Null si no hace nada (interruptor apagado, SP del domingo en marcha o empresa que el SP no procesa). Si falla,
        /// deja el error en ELMAH y relanza para que Hangfire reintente.
        /// </summary>
        internal static ResultadoEscrituraPrecioMedio ProcesarProducto(bool activo, Func<ServicioIncrementalPreciosMedios> crearServicio,
            string empresa, string producto)
        {
            if (!activo)
            {
                return null;
            }
            ServicioIncrementalPreciosMedios servicio = crearServicio();
            EjecucionSPPreciosMedios sp = servicio.UltimaEjecucionSP();
            if (sp != null && sp.EnEjecucion)
            {
                return null; // lo recoge la pasada nocturna (la compra queda con Fecha Modificación posterior a la marca)
            }
            try
            {
                return servicio.RecalcularProducto(empresa, producto);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Precios medios #547] No se ha podido recalcular el producto {producto?.Trim()} " +
                    $"de la empresa {empresa?.Trim()}: {ex.Message}", ex), USUARIO_ELMAH);
                throw;
            }
        }

        /// <summary>Productos encolados (lista vacía si está apagado).</summary>
        internal static IReadOnlyList<string> ProcesarPedido(bool activo, Func<ServicioIncrementalPreciosMedios> crearServicio,
            string empresa, int pedido, Action<string, string> encolarProducto)
        {
            if (!activo)
            {
                return new List<string>();
            }
            ServicioIncrementalPreciosMedios servicio = crearServicio();
            IReadOnlyList<string> productos = servicio.ProductosDelPedidoCompra(empresa, pedido);
            foreach (string producto in productos)
            {
                encolarProducto(empresa, producto);
            }
            return productos;
        }

        /// <summary>True si ha encolado.</summary>
        internal static bool Encolar(bool activo, string empresa, int pedido, Action<string, int> encolarPedido)
        {
            if (!activo)
            {
                return false;
            }
            try
            {
                encolarPedido(empresa?.Trim(), pedido);
                return true;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Precios medios #547] No se ha podido encolar el recálculo del pedido de compra {pedido}: " +
                    ex.Message, ex), USUARIO_ELMAH);
                return false;
            }
        }

        #endregion
    }
}
