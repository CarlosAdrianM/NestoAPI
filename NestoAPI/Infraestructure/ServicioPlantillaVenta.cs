using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Infraestructure
{
    public class ServicioPlantillaVenta : IServicioPlantillaVenta
    {
        public HashSet<string> CargarProductosBonificables()
        {
            return new HashSet<string>
            {
                "15477",
                "27240",
                "25539",
                "18003",
                "18004",
                "22161",
                "25401",
                "33787",
                "27067",
                "21722",
                "32716",
                "24883",
                "20992",
                "35975",
                "39196",
                "39197",
                "20892",
                "39200",
                "38648",
                "24392",
                "23789",
                "22045",
                "22530",
                "22696",
                "16627",
                "32988",
                "26999",
                "12537",
                "20706",
                "20705",
                "24807",
                "26692",
                "20459",
                "21492",
                "39858",
                "25720",
                "25279",
                "25966",
                "24885",
                "24470",
                "39604",
                "34081",
                "34080",
                "29803",
                "29804",
                "24955"
            };
        }

        public HashSet<string> CargarProductosYaComprados(string cliente)
        {
            using (NVEntities db = new NVEntities())
            {
                try
                {
                    DateTime fechaDesde = DateTime.Today.AddYears(-1);
                    HashSet<string> productos = db.LinPedidoVtas.
                    Where(x => x.Nº_Cliente == cliente && x.Base_Imponible > 0 && x.Fecha_Factura > fechaDesde && x.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO).
                    Select(x => x.Producto).ToHashSet();
                    return productos;
                }
                catch (Exception e)
                {
                    throw new Exception("No se han pedido leer los productos ya comprados", e);
                }                
            }
        }
    }
}