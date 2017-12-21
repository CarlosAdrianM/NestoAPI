namespace NestoAPI.Infraestructure
{
    #region Condiciones
    public class OtrosAparatosNoPuedeLlevarDescuento : ICondicionPrecioDescuento
    {
        public bool precioAceptado(PrecioDescuentoProducto precio)
        {
            // Los productos del grupo Otros aparatos no pueden tener ningún tipo de descuento
            if (precio.descuentoReal > 0 && precio.producto.SubGrupo.ToLower() == "acp")
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
                precio.motivo = "El grupo Otros Aparatos no puede llevar ningún precio especial ni descuento";

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
