using Hangfire;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Picking
{
    /// <summary>
    /// NestoAPI#361: el picking de CIERRE del día (el de las 11h), que hasta ahora lanzaba una
    /// tarea del Task Scheduler llamando a la API por HTTP.
    ///
    /// Dos cosas lo hacían frágil, y las dos se arreglan aquí:
    ///
    /// 1. <b>Nadie se enteraba del resultado.</b> Al no haber ninguna pantalla delante, "no ha
    ///    salido picking" significaba tres cosas distintas —no había nada, falló algo, o la tarea
    ///    ni se ejecutó— y el almacén acababa preguntando a Informática. Ahora se avisa por correo
    ///    de los dos primeros casos, así que el silencio solo puede significar el tercero.
    ///
    /// 2. <b>El horizonte de entrega salía del reloj.</b> Ver <c>GestorPicking.SacarPicking(DateTime)</c>:
    ///    arrancar a las 11:00:01 en vez de a las 10:59:59 no dejaba el picking vacío, lo sacaba DE
    ///    MÁS. Ahora el horizonte se declara (hoy) y da igual el segundo en que arranque.
    ///
    /// Con (2) resuelto, llegar tarde dejó de costar nada, que es justo lo que permite moverlo a
    /// Hangfire: el cron dispara con unos segundos de retraso natural y eso ya no importa. Es más,
    /// ahora es una ventaja, porque da margen a que commiteen los pedidos metidos justo antes del
    /// corte.
    /// </summary>
    public class PickingJobsService
    {
        /// <summary>
        /// Punto de entrada para Hangfire (job recurrente <c>picking-cierre-diario</c>) y para el
        /// endpoint manual <c>api/Picking/Automatico</c>. Una sola implementación para los dos.
        ///
        /// <para>SIN REINTENTO AUTOMÁTICO a propósito. No es por riesgo —el picking es idempotente,
        /// las líneas ya asignadas se filtran por <c>Picking == null || Picking == 0</c>— sino para
        /// no mandarle al almacén un correo por cada intento. Si falla, se ve en rojo en el
        /// dashboard de Hangfire y se relanza desde ahí con un clic; llegar tarde no tiene ningún
        /// coste.</para>
        /// </summary>
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static List<PedidoPicking> SacarPickingDeCierre()
        {
            GestorPicking gestorPicking = CrearGestor();

            try
            {
                // El horizonte es un DATO, no una lectura del reloj: este es el picking de HOY.
                gestorPicking.SacarPicking(DateTime.Today);
            }
            catch (Exception ex)
            {
                CrearAvisador().Avisar(ex, DateTime.Now);

                // "No había nada que sacar" es un resultado NORMAL, no un fallo: el almacén ya está
                // avisado y el job no debe quedarse en rojo por ello (si no, el dashboard mentiría
                // justo los días tranquilos). Cualquier otra cosa sí se relanza, para que Hangfire
                // la registre y se vea en el dashboard.
                if (AvisadorPickingAutomatico.EsPickingSinTrabajo(ex))
                {
                    return new List<PedidoPicking>();
                }

                throw;
            }

            return gestorPicking.PedidosEnPicking();
        }

        internal static GestorPicking CrearGestor()
        {
            ModulosPicking modulos = new ModulosPicking
            {
                rellenadorPicking = new RellenadorPickingService(),
                rellenadorStocks = new RellenadorStocksService(),
                rellenadorUbicaciones = new RellenadorUbicacionesService(),
                finalizador = new FinalizadorPicking()
            };

            return new GestorPicking(modulos);
        }

        internal static AvisadorPickingAutomatico CrearAvisador()
        {
            return new AvisadorPickingAutomatico(
                new ServicioCorreoElectronico(),
                new LectorParametrosUsuario());
        }
    }
}
