using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.RecursosHumanos;

namespace NestoAPI.Infraestructure
{
    public class GestorAgenciasGlovo : IGestorAgencias
    {
        public async Task<RespuestaAgencia> SePuedeServirPedido(PedidoVentaDTO pedido, IServicioAgencias servicio, IGestorStocks gestorStocks)
        {
            DateTime hora = servicio.HoraActual();
            if (hora.Hour < 9 || hora.Hour > 18)
            {
                return null;
            }
            if (hora.DayOfWeek == DayOfWeek.Saturday || hora.DayOfWeek == DayOfWeek.Sunday)
            {
                return null;
            }
            
            string codigoPostal = servicio.LeerCodigoPostal(pedido);
            if (!codigoPostal.StartsWith("28"))
            {
                return null;
            }

            var almacenPedido = pedido?.Lineas?.First().almacen;
            if (GestorFestivos.EsFestivo(hora, almacenPedido))
            {
                return null;
            }

            RespuestaAgencia respuestaAgencia = PortesPorCodigoPostal(codigoPostal);
            if (respuestaAgencia == null || string.IsNullOrEmpty(respuestaAgencia.Almacen))
            {
                return null;
            }
            respuestaAgencia.CondicionesPagoValidas = true;
            if (pedido.ccc == null && (pedido.plazosPago == null || pedido.plazosPago.Trim() != "PRE"))
            {
                respuestaAgencia.CondicionesPagoValidas = false;
            }

            if (!gestorStocks.HayStockDisponibleDeTodo(pedido, respuestaAgencia.Almacen) || !pedido.Lineas.Any())
            {
                return null;
            }

            /*
            // No  hay motivo por el que no pueda salir, así que calculamos todo
            // TO DO: implementar resultado
            RespuestaAgencia respuestaDireccionGoogle = await servicio.LeerDireccionPedidoGoogleMaps(pedido);
            respuestaAgencia.DireccionFormateada = respuestaDireccionGoogle.DireccionFormateada;
            respuestaAgencia.Longitud = respuestaDireccionGoogle.Longitud; // no se si vale para algo
            respuestaAgencia.Latitud = respuestaDireccionGoogle.Latitud; // no se si vale para algo
            */

            TelemetryClient telemetry = new TelemetryClient();
            telemetry.Context.User.Id = pedido.Usuario;
            if (respuestaAgencia.CondicionesPagoValidas)
            {
                telemetry.TrackEvent("OfrecidoGlovo");
            }
            else
            {
                telemetry.TrackEvent("EntregaUrgenteSinPrepago");
            }

            return respuestaAgencia;
        }

        public static RespuestaAgencia PortesPorCodigoPostal(string codigoPostal)
        {
            // de momento lo hacemos así pero hay que refactorizar esto con diferentes clases cuando ya esté todo estable

            string[] codigosPostalesShopopop = { "28100", "28109", "28108", "28701", "28702", "28703", "28708", "28033", "28034", "28043", "28049", "28050", "28055" };
            string[] codigosPostalesRams = { "28001", "28002", "28003", "28004", "28005", "28006", "28007", "28008", "28009", "28010", "28012", "28013", "28014", "28015", "28016", "28020", "28028", "28029", "28036", "28039", "28040", "28045", "28046" };

            if (codigosPostalesShopopop.Contains(codigoPostal))
            {
                return new RespuestaAgencia
                {
                    Almacen = Constantes.Almacenes.ALCOBENDAS,
                    Coste = 7.9M
                };
            }

            if (codigosPostalesRams.Contains(codigoPostal))
            {
                return new RespuestaAgencia
                {
                    Almacen = Constantes.Almacenes.REINA,
                    Coste = 5.5M
                };
            }

            return new RespuestaAgencia();
        }
    }
}