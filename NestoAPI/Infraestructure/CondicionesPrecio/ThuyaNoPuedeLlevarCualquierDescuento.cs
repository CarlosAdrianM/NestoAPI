namespace NestoAPI.Infraestructure
{
    #region Condiciones
    public class ThuyaNoPuedeLlevarCualquierDescuento : ICondicionPrecioDescuento
    {
        public bool precioAceptado(PrecioDescuentoProducto precio)
        {
            // Los productos de la familia Du no pueden tener más de un 15% de descuento
            if (precio.producto.Familia.ToLower().Trim() == "thuya" && precio.descuentoReal > (decimal).15)
            {
                precio.precioCalculado = (decimal)precio.producto.PVP;
                precio.descuentoCalculado = (decimal).15;
                precio.motivo = "No se puede hacer un descuento superior al 15% en Thuya";
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
