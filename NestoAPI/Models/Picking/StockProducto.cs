using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class StockProducto
    {
        public string Producto { get; set; }
        public int StockDisponible { get; set; }
        public int StockTienda { get; set; }
        /// <summary>NestoAPI#482 (modo 3): unidades en camino entre almacenes (PreExtrProducto con
        /// NºTraspaso, reposición generada y sin contabilizar), en cualquier sentido. El origen ya las
        /// descontó en ExtractoProducto, así que no están en StockDisponible ni en StockTienda.</summary>
        public int EnCamino { get; set; }

        /// <summary>Copia con los valores de ANTES de repartir: GestorReservasStock consume
        /// StockDisponible y StockTienda al reservar, y el modo 3 necesita el pool inicial.</summary>
        public StockProducto Clonar()
        {
            return new StockProducto
            {
                Producto = Producto,
                StockDisponible = StockDisponible,
                StockTienda = StockTienda,
                EnCamino = EnCamino
            };
        }
    }
}