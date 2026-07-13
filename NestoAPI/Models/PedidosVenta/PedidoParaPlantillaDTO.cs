using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// Nesto#397: el pedido YA en forma de plantilla de venta. La inversión PedidoVentaDTO →
    /// plantilla (colapsar las 2 líneas de una oferta en una, clasificar los regalos Ganavisiones)
    /// vive en el backend (<see cref="Infraestructure.PedidosVenta.ConvertidorPedidoAPlantilla"/>)
    /// para que Nesto y NestoApp solo mapeen esta estructura a sus ViewModels, sin duplicarla.
    /// </summary>
    public class PedidoParaPlantillaDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        // Nº del pedido en edición: la plantilla en modo edición guarda con PUT sobre este número.
        public int NumeroPedido { get; set; }
        public bool EsPresupuesto { get; set; }
        public string FormaPago { get; set; }
        public string PlazosPago { get; set; }
        public string ComentarioPicking { get; set; }
        public string Comentarios { get; set; }
        public string Ruta { get; set; }
        public bool ServirJunto { get; set; }
        public bool MantenerJunto { get; set; }
        // De las líneas (la cabecera no las lleva): entrega mínima y almacén de la primera línea real.
        public DateTime? FechaEntrega { get; set; }
        public string Almacen { get; set; }
        public List<LineaParaPlantillaDTO> Lineas { get; set; } = new List<LineaParaPlantillaDTO>();
        public List<RegaloParaPlantillaDTO> Regalos { get; set; } = new List<RegaloParaPlantillaDTO>();
    }

    /// <summary>
    /// Una línea de la plantilla: la parte de pago (Cantidad/Precio/Descuento) y, si la línea
    /// pertenece a una oferta, la parte de oferta colapsada (CantidadOferta y, si la oferta es
    /// personalizada tipo "2ª unidad al 50 %", PersonalizarOferta + PrecioOferta/DescuentoOferta).
    /// </summary>
    public class LineaParaPlantillaDTO
    {
        public string Producto { get; set; }
        public string Texto { get; set; }
        public int Cantidad { get; set; }
        public decimal Precio { get; set; }
        public decimal Descuento { get; set; }
        public bool AplicarDescuento { get; set; }
        public int CantidadOferta { get; set; }
        public bool PersonalizarOferta { get; set; }
        public decimal PrecioOferta { get; set; }
        public decimal DescuentoOferta { get; set; }
        // Ids de LinPedidoVta originales, para que el PUT del modo edición actualice las líneas
        // existentes en vez de recrearlas (0/null = línea que no existía).
        public int IdLineaPago { get; set; }
        public int? IdLineaOferta { get; set; }
    }

    /// <summary>Regalo Ganavisiones (confirmado contra la tabla Ganavision por el GET, #279).</summary>
    public class RegaloParaPlantillaDTO
    {
        public string Producto { get; set; }
        public string Texto { get; set; }
        public int Cantidad { get; set; }
        public int IdLinea { get; set; }
    }
}
