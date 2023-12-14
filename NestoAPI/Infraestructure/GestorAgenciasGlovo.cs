using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure
{
    public class GestorAgenciasGlovo : IGestorAgencias
    {
        public async Task<RespuestaAgencia> SePuedeServirPedido(PedidoVentaDTO pedido, IServicioAgencias servicio, IGestorStocks gestorStocks)
        {
            //string[] codigosPostalesPermitidos = { "28004", "28005", "28008", "28012", "28013", "28014", "28015" };
            string codigoPostal = servicio.LeerCodigoPostal(pedido);
            //if (!codigosPostalesPermitidos.Contains(codigoPostal))
            if (!codigoPostal.StartsWith("280"))
            {
                return null;
            }

            if (pedido.ccc == null && (pedido.plazosPago == null || pedido.plazosPago.Trim() != "PRE"))
            {
                return null;
            }

            //string[] almacenesPermitidos = { Constantes.Almacenes.REINA };
            //foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido)
            //{
            //    if (!almacenesPermitidos.Contains(linea.almacen))
            //    {
            //        return false;
            //    }
            //}

            DateTime hora = servicio.HoraActual();
            if (hora.Hour < 9 || hora.Hour > 18)
            {
                return null;
            }
            if (hora.DayOfWeek == DayOfWeek.Saturday || hora.DayOfWeek == DayOfWeek.Sunday)
            {
                return null;
            }


            if (!gestorStocks.HayStockDisponibleDeTodo(pedido) || !pedido.Lineas.Any())
            {
                return null;
            }


            // No  hay motivo por el que no pueda salir, así que calculamos todo
            // TO DO: implementar resultado
            RespuestaAgencia respuesta = await servicio.LeerDireccionPedidoGoogleMaps(pedido);

            TelemetryClient telemetry = new TelemetryClient();
            telemetry.Context.User.Id = pedido.Usuario;
            telemetry.TrackEvent("OfrecidoGlovo");

            return new RespuestaAgencia {
                DireccionFormateada = respuesta.DireccionFormateada,
                Longitud = respuesta.Longitud,
                Latitud = respuesta.Latitud,
                Coste = respuesta.Coste
            };
        }
    }
}