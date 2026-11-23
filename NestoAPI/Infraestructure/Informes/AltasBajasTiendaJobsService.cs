using NestoAPI.Models;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// NestoAPI#886: el día 1 de cada mes a las 8:15 de Madrid, a cada tienda (REI, ALC) el correo con lo dado de alta y de baja en su
    /// stock el mes natural anterior, con copia a Compras (<see cref="EnviadorAltasBajasTienda"/>, el mismo camino que el POST a mano).
    /// Sin movimientos en una tienda, a esa no se le manda nada. Un fallo en una tienda no impide la otra (ELMAH).
    /// </summary>
    public class AltasBajasTiendaJobsService
    {
        public const string JOB_ID = "tiendas-altas-bajas-mensual";
        /// <summary>El día 1 de cada mes a las 8:15.</summary>
        public const string CRON = "15 8 1 * *";

        private readonly IInformesService servicio;
        private readonly IServicioCorreoElectronico correo;

        internal AltasBajasTiendaJobsService(IInformesService servicio, IServicioCorreoElectronico correo)
        {
            this.servicio = servicio;
            this.correo = correo;
        }

        /// <summary>Punto de entrada de Hangfire.</summary>
        public static async Task Enviar()
        {
            using (NVEntities db = new NVEntities())
            {
                _ = await new AltasBajasTiendaJobsService(new InformesService(db), new ServicioCorreoElectronico())
                    .EnviarMesAnteriorAsync(DateTime.Today)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>Lo que ha pasado con cada tienda (una fila por tienda que no ha fallado). Nunca lanza.</summary>
        public async Task<List<CorreoAltasBajasTiendaDTO>> EnviarMesAnteriorAsync(DateTime hoy)
        {
            (DateTime desde, DateTime hasta) = GestorAltasBajasTienda.MesAnterior(hoy);
            var resultados = new List<CorreoAltasBajasTiendaDTO>();
            var enviador = new EnviadorAltasBajasTienda(servicio, correo);
            foreach (string almacen in GestorFacturacionTienda.TIENDAS)
            {
                try
                {
                    CorreoAltasBajasTiendaDTO resultado = await enviador.EnviarAsync(almacen, desde, hasta).ConfigureAwait(false);
                    if (resultado.NumeroMovimientos > 0 && !resultado.Enviado)
                    {
                        ElmahHelper.Log(new Exception($"[Altas y bajas tienda #886] {almacen} {desde:dd/MM/yyyy}-{hasta:dd/MM/yyyy}: {resultado.Mensaje}"));
                    }
                    resultados.Add(resultado);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Altas y bajas tienda #886] No se ha podido mandar el correo de {almacen} " +
                        $"({desde:dd/MM/yyyy}-{hasta:dd/MM/yyyy}): {ex.Message}", ex));
                }
            }
            return resultados;
        }
    }
}
