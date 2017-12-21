namespace NestoAPI.Infraestructure
{
    #region Condiciones
    public class RamasonNoPuedeLlevarNingunDescuento : ICondicionPrecioDescuento
    {
        public bool precioAceptado(PrecioDescuentoProducto precio)
        {
            // Los productos de la familia Ramason no aceptan ningún tipo de descuento
            if (precio.descuentoReal > 0 && precio.producto.Familia.ToLower().Trim() == "ramason")
            {
                if (precio.aplicarDescuento)
                {
                    precio.aplicarDescuento = false;
                }
                if (precio.precioCalculado != (decimal)precio.producto.PVP)
                {
                    precio.precioCalculado = (decimal)precio.producto.PVP;
                }
                if (precio.descuentoCalculado != 0)
                {
                    precio.descuentoCalculado = 0;
                }
                precio.motivo = "No se puede hacer ningún precio especal ni descuento en productos de Ramasón";

                return false;
            }
            else
            {
                return true;
            }
        }
    }
    #endregion
}
