namespace NestoAPI.Models
{
    /// <summary>
    /// Clase parcial de LinPedidoVta para métodos de clonación
    /// </summary>
    public partial class LinPedidoVta
    {
        /// <summary>
        /// Clona la línea cambiando la empresa y número de pedido destino.
        /// Este método copia TODAS las propiedades escalares.
        /// VENTAJA: Si se agregan nuevas propiedades al modelo, se copiarán automáticamente.
        /// </summary>
        /// <param name="empresaDestino">Empresa destino</param>
        /// <param name="numeroPedidoDestino">Número de pedido destino</param>
        /// <returns>Nueva línea clonada</returns>
        public LinPedidoVta ClonarParaEmpresa(string empresaDestino, int numeroPedidoDestino)
        {
            return new LinPedidoVta
            {
                // Clave primaria - cambiamos empresa y número
                Empresa = empresaDestino,
                Número = numeroPedidoDestino,
                Nº_Orden = this.Nº_Orden,  // Este NO da error porque es un objeto nuevo

                // Todas las demás propiedades escalares
                Nº_Cliente = this.Nº_Cliente,
                Contacto = this.Contacto,
                TipoLinea = this.TipoLinea,
                Producto = this.Producto,
                Almacén = this.Almacén,
                Fecha_Entrega = this.Fecha_Entrega,
                Texto = this.Texto,
                Cantidad = this.Cantidad,
                Precio = this.Precio,
                IVA = this.IVA,
                PorcentajeIVA = this.PorcentajeIVA,
                PorcentajeRE = this.PorcentajeRE,
                Bruto = this.Bruto,
                DescuentoCliente = this.DescuentoCliente,
                DescuentoProducto = this.DescuentoProducto,
                Descuento = this.Descuento,
                DescuentoPP = this.DescuentoPP,
                SumaDescuentos = this.SumaDescuentos,
                ImporteDto = this.ImporteDto,
                Base_Imponible = this.Base_Imponible,
                ImporteIVA = this.ImporteIVA,
                ImporteRE = this.ImporteRE,
                Total = this.Total,
                Aplicar_Dto = this.Aplicar_Dto,
                Delegación = this.Delegación,
                Forma_Venta = this.Forma_Venta,
                Nº_Albarán = this.Nº_Albarán,
                Fecha_Albarán = this.Fecha_Albarán,
                Nº_Factura = this.Nº_Factura,
                Fecha_Factura = this.Fecha_Factura,
                Picking = this.Picking,
                Estado = this.Estado,
                Grupo = this.Grupo,
                SubGrupo = this.SubGrupo,
                NºOferta = this.NºOferta,
                GeneraBonificación = this.GeneraBonificación,
                Familia = this.Familia,
                TipoExclusiva = this.TipoExclusiva,
                YaFacturado = this.YaFacturado,
                VtoBueno = this.VtoBueno,
                Coste = this.Coste,
                BlancoParaBorrar = this.BlancoParaBorrar,
                Reponer = this.Reponer,
                PrecioTarifa = this.PrecioTarifa,
                Recoger = this.Recoger,
                LineaParcial = this.LineaParcial,
                EstadoProducto = this.EstadoProducto,
                CentroCoste = this.CentroCoste,
                Departamento = this.Departamento,
                NumSerie = this.NumSerie,
                Usuario = this.Usuario,
                Fecha_Modificación = this.Fecha_Modificación
                // NO copiamos: CabPedidoVta, Empresa1, Cliente, Familia1, SubGruposProducto,
                // FormasVenta, PedidosEspeciales, CabFacturaVta, VendedorLinPedidoVtas,
                // GruposProducto (propiedades de navegación)
                // NO copiamos: RowVersion (debe ser nuevo)
            };
        }
    }
}
