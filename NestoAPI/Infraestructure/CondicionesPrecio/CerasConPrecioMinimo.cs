namespace NestoAPI.Infraestructure
{
    #region Condiciones
    public class CerasConPrecioMinimo : ICondicionPrecioDescuento
    {
        public bool precioAceptado(PrecioDescuentoProducto precio)
        {
            // Ponemos el trim aquí para evitar tener que ponerlo en todas las comparaciones
            precio.producto.Número = precio.producto.Número.Trim();

            // Clean & Easy
            if (precio.producto.Grupo == "COS" 
                && precio.producto.SubGrupo == "304" 
                && precio.producto.Familia.ToLower().Trim() == "clean&easy"
                && (precio.precioCalculado < precio.producto.PVP || precio.descuentoReal > 0))
            {
                precio.aplicarDescuento = false;
                precio.precioCalculado = (decimal)precio.producto.PVP;
                precio.descuentoCalculado = 0;
                precio.motivo = "No se puede hacer ningún descuento en las ceras de Clean & Easy";
                return false;
            }

            // Tessiline
            if (precio.producto.Grupo == "COS"
                && precio.producto.SubGrupo == "304"
                && precio.producto.Familia.ToLower().Trim() == "tessiline"
                && (precio.precioCalculadoDeFicha < (decimal).79))
            {
                precio.precioCalculado = (decimal).79;
                precio.descuentoCalculado = 0;
                precio.motivo = "No se pueden dejar las ceras de Tessiline a menos de 0,79 €";
                return false;
            }

            // Uso Profesional
            if ((precio.producto.Número == "27092" || precio.producto.Número == "27093")
                && (precio.precioCalculadoDeFicha < (decimal)1.19))
            {
                precio.precioCalculado = (decimal)1.19;
                precio.descuentoCalculado = 0;
                precio.motivo = "Esa cera de Uso Profesional no se puede dejar a menos de 1,19 €";
                return false;
            }
            if ((precio.producto.Número == "33728")
                && (precio.precioCalculadoDeFicha < (decimal)7.95))
            {
                precio.precioCalculado = (decimal)7.95;
                precio.descuentoCalculado = 0;
                precio.motivo = "La cera malva de Uso Profesional no se puede dejar a menos de 7,95 €";
                return false;
            }

            // Fama
            if ((precio.producto.Número == "32254" || precio.producto.Número == "34411" || precio.producto.Número == "22501")
                && (precio.precioCalculadoDeFicha < (decimal)1.65))
            {
                precio.precioCalculado = (decimal)1.65;
                precio.descuentoCalculado = 0;
                precio.motivo = "Los cartuchos de Fama Fabré no se pueden dejar por debajo de 1,65 €";
                return false;
            }

            // Maystar
            if ((precio.producto.Número == "23050"
                || precio.producto.Número == "12627"
                || precio.producto.Número == "25935"
                || precio.producto.Número == "12633"
                || precio.producto.Número == "21885"
                || precio.producto.Número == "21376"
                || precio.producto.Número == "21112"
                || precio.producto.Número == "33138"
                || precio.producto.Número == "31916"
                || precio.producto.Número == "12634"
                || precio.producto.Número == "15930"
                || precio.producto.Número == "22450"
                || precio.producto.Número == "33068"
                || precio.producto.Número == "21075"
                || precio.producto.Número == "23727")
                && (precio.precioCalculadoDeFicha < (decimal).99))
            {
                precio.precioCalculado = (decimal).99;
                precio.descuentoCalculado = 0;
                precio.motivo = "Los cartuchos de Maystar no se pueden dejar por debajo de 0,99 €";
                return false;
            }
            if ((precio.producto.Número == "12640" || precio.producto.Número == "22624")
                && (precio.precioCalculadoDeFicha < (decimal)4.45))
            {
                precio.precioCalculado = (decimal)4.45;
                precio.descuentoCalculado = 0;
                precio.motivo = "Las latas de cera de 500ml de Maystar no se pueden dejar por debajo de 4,45 €";
                return false;
            }
            if ((precio.producto.Número == "19205" || precio.producto.Número == "32371")
            && (precio.precioCalculadoDeFicha < (decimal)6.45))
            {
                precio.precioCalculado = (decimal)6.45;
                precio.descuentoCalculado = 0;
                precio.motivo = "Las latas de cera de 800ml de Maystar no se pueden dejar por debajo de 6,45 €";
                return false;
            }
            if ((precio.producto.Número == "17031"
                || precio.producto.Número == "19828"
                || precio.producto.Número == "21044"
                || precio.producto.Número == "12637"
                || precio.producto.Número == "15789"
                || precio.producto.Número == "22535"
                || precio.producto.Número == "24279"
                || precio.producto.Número == "20084"
                || precio.producto.Número == "21661"
                || precio.producto.Número == "12645"
                || precio.producto.Número == "22982"
                || precio.producto.Número == "32745")
                && (precio.precioCalculadoDeFicha < (decimal)6.95))
            {
                precio.precioCalculado = (decimal)6.95;
                precio.descuentoCalculado = 0;
                precio.motivo = "Esta cera de Maystar no se pueden dejar por debajo de 6,95 €";
                return false;
            }

            // Eva Visnú
            if ((precio.producto.Número == "12515"
                || precio.producto.Número == "12537"
                || precio.producto.Número == "20706"
                || precio.producto.Número == "20705"
                || precio.producto.Número == "24807")
                && (precio.precioCalculadoDeFicha < (decimal).99))
            {
                precio.precioCalculado = (decimal).99;
                precio.descuentoCalculado = 0;
                precio.motivo = "Los cartuchos de Eva Visnú no se pueden dejar por debajo de 0,99 €";
                return false;
            }
            if ((precio.producto.Número == "26692")
                && (precio.precioCalculadoDeFicha < (decimal)5.95))
            {
                precio.precioCalculado = (decimal)5.95;
                precio.descuentoCalculado = 0;
                precio.motivo = "La Cera Esmeralda de Eva Visnú no se puede dejar por debajo de 5,95 €";
                return false;
            }
            if ((precio.producto.Número == "20258" || precio.producto.Número == "25391")
                && (precio.precioCalculadoDeFicha < (decimal)5.45))
            {
                precio.precioCalculado = (decimal)5.45;
                precio.descuentoCalculado = 0;
                precio.motivo = "Las latas de 500ml de cera de Eva Visnú no se pueden dejar por debajo de 5,45 €";
                return false;
            }
            if ((precio.producto.Número == "16631" || precio.producto.Número == "25392")
                && (precio.precioCalculadoDeFicha < (decimal)7.75))
            {
                precio.precioCalculado = (decimal)7.75;
                precio.descuentoCalculado = 0;
                precio.motivo = "Las latas de 800ml de cera de Eva Visnú no se pueden dejar por debajo de 7,75 €";
                return false;
            }
            if ((precio.producto.Número == "20459" || precio.producto.Número == "21492")
                && (precio.precioCalculadoDeFicha < (decimal)6.40))
            {
                precio.precioCalculado = (decimal)6.40;
                precio.descuentoCalculado = 0;
                precio.motivo = "Este kilo de cera de Eva Visnú no se puede dejar por debajo de 6,40 €";
                return false;
            }

            // Depil OK
            if ((precio.producto.Número == "18624" || precio.producto.Número == "18705")
                && (precio.precioCalculadoDeFicha < (decimal).89))
            {
                precio.precioCalculado = (decimal).89;
                precio.descuentoCalculado = 0;
                precio.motivo = "El Botellín Estándar de Depil OK no se pueden dejar por debajo de 0,89 €";
                return false;
            }
            if ((precio.producto.Número == "17954"
                || precio.producto.Número == "18709"
                || precio.producto.Número == "17770"
                || precio.producto.Número == "17859"
                || precio.producto.Número == "19388")
                && (precio.precioCalculadoDeFicha < (decimal)1.05))
            {
                precio.precioCalculado = (decimal)1.05;
                precio.descuentoCalculado = 0;
                precio.motivo = "Este cartucho cerrado de Depil OK no se puede dejar por debajo de 1,05 €";
                return false;
            }
            if ((precio.producto.Número == "35666"
                || precio.producto.Número == "19455"
                || precio.producto.Número == "24076"
                || precio.producto.Número == "22665"
                || precio.producto.Número == "24077"
                || precio.producto.Número == "21176"
                || precio.producto.Número == "35667")
                && (precio.precioCalculadoDeFicha < (decimal)1.10))
            {
                precio.precioCalculado = (decimal)1.10;
                precio.descuentoCalculado = 0;
                precio.motivo = "Este cartucho cerrado de Depil OK no se puede dejar por debajo de 1,10 €";
                return false;
            }
            if ((precio.producto.Número == "19989")
                            && (precio.precioCalculadoDeFicha < (decimal)5.95))
            {
                precio.precioCalculado = (decimal)5.95;
                precio.descuentoCalculado = 0;
                precio.motivo = "La Cera Azul de Depil OK no se puede dejar por debajo de 5,95 €";
                return false;
            }
            if ((precio.producto.Número == "32317")
                && (precio.precioCalculadoDeFicha < (decimal)9.45))
            {
                precio.precioCalculado = (decimal)9.45;
                precio.descuentoCalculado = 0;
                precio.motivo = "La Cera Extrafina de Depil OK no se puede dejar por debajo de 9,45 €";
                return false;
            }
            if ((precio.producto.Número == "19730")
                && (precio.precioCalculadoDeFicha < (decimal)8.95))
            {
                precio.precioCalculado = (decimal)8.95;
                precio.descuentoCalculado = 0;
                precio.motivo = "La Cera Fría Miel de Depil OK no se puede dejar por debajo de 8,95 €";
                return false;
            }
            if ((precio.producto.Número == "21664")
                && (precio.precioCalculadoDeFicha < (decimal)7.25))
            {
                precio.precioCalculado = (decimal)7.25;
                precio.descuentoCalculado = 0;
                precio.motivo = "La Cera Violeta Lavanda de Depil OK no se puede dejar por debajo de 7,25 €";
                return false;
            }
            if ((precio.producto.Número == "27496"
                || precio.producto.Número == "20339"
                || precio.producto.Número == "21774"
                || precio.producto.Número == "19643"
                || precio.producto.Número == "20188")
                && (precio.precioCalculadoDeFicha < (decimal)6.95))
            {
                precio.precioCalculado = (decimal)6.95;
                precio.descuentoCalculado = 0;
                precio.motivo = "Este kilo de cera de Depil OK no se puede dejar por debajo de 6,95 €";
                return false;
            }
            
            // Si ninguna norma ha echado el precio para atrás, lo damos por bueno
            return true;
        }
    }
    #endregion
}
