using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public class GestorPrecios
    {
        public static bool calcularAplicarDescuento(Producto producto)
        {
            return calcularAplicarDescuento(producto.Aplicar_Dto, producto.Familia, producto.SubGrupo);
        }
        
        public static bool calcularAplicarDescuento(bool aplicarDescuento, string familia, string subGrupo)
        {
            // Esto podríamos sacarlo a otra clase que sea más fácil de mantener
            // if (!aplicarDescuento || familia == "Ramason" || subGrupo == "ACP")
            if (!aplicarDescuento)
            {
                return false;
            } else
            {
                return true;
            }

        }

        public static void calcularDescuentoProducto(PrecioDescuentoProducto datos)
        {

            DescuentosProducto dtoProducto;

            datos.descuentoCalculado = 0;
            datos.precioCalculado = (decimal)datos.producto.PVP;

            datos.aplicarDescuento = calcularAplicarDescuento(datos.aplicarDescuento, datos.producto.Familia, datos.producto.SubGrupo);

            // En Nesto Viejo, si no tiene el aplicar descuento marcado, solo calcula precios especiales, pero no descuentos
            // Ahora hacemos que no calcule nada, por eso lo pongo aquí arriba.
            if (!datos.aplicarDescuento)
            {
                return;
            }


            using (NVEntities db = new NVEntities()) {
                // AQUÍ CALCULA PRECIOS, NO DESCUENTOS
                //select precio from descuentosproducto with (nolock) where [nº cliente]='15191     ' and contacto='0  ' and [nº producto]= '29487' and empresa='1  ' AND CANTIDADMÍNIMA<=1

                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Contacto == datos.contacto && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad);
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }
                //select precio from descuentosproducto with (nolock) where [nº cliente]='15191     '  and [nº producto]= '29487' and empresa='1  ' AND CantidadMínima<=1
                //select recargopvp from clientes with (nolock) where empresa='1  ' and [nº cliente]='15191     ' and contacto='0  '
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente]='15191     ' and contacto='0  ' order by cantidadminima desc
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente]='15191     '  order by cantidadminima desc
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad);
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }
                //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente] is null and [nºproveedor] is null order by cantidadminima desc
                dtoProducto = db.DescuentosProductoes.OrderByDescending(d => d.CantidadMínima).FirstOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.Nº_Cliente == null && d.NºProveedor == null);
                if (dtoProducto != null && dtoProducto.Precio < datos.precioCalculado)
                {
                    datos.precioCalculado = (decimal)dtoProducto.Precio;
                }

                // CALCULA DESCUENTOS
                //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente] is null and nºproveedor is null and familia is null
                //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente] is null and  nºproveedor is null and familia is null
                //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente]='15191     ' and nºproveedor is null and familia is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.Familia == null);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }

                //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente]='15191     ' and nºproveedor is null and familia is null

                // AGAIN AND AGAIN AND AGAIN...
                //select isnull(max(descuento),0) from descuentosproducto where [nº cliente]='15191     ' and empresa='1  ' and grupoproducto='PEL' and cantidadmínima<=1 and familia is null and nºproveedor is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == null && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == datos.producto.Grupo);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }
                //select * from descuentosproducto where empresa='1  ' and familia='Lisap     ' and [nº cliente]='15191     ' and nºproveedor is null and grupoproducto is null
                //select isnull(max(descuento),0) from descuentosproducto where [nº cliente]='15191     ' and empresa='1  ' and familia='Lisap     ' and cantidadmínima<=1 and nºproveedor is null  and grupoproducto is null
                dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Cliente == datos.cliente && d.Familia == datos.producto.Familia && d.CantidadMínima <= datos.cantidad && d.NºProveedor == null && d.GrupoProducto == null);
                if (dtoProducto != null && dtoProducto.Descuento > datos.descuentoCalculado)
                {
                    datos.descuentoCalculado = dtoProducto.Descuento;
                }
                //select * from descuentosproducto where empresa='1  ' and familia='Lisap     ' and [nº cliente]='15191     ' and grupoproducto='PEL' and nºproveedor is null

                if (datos.precioCalculado < datos.producto.PVP * (1 - datos.descuentoCalculado))
                {
                    datos.descuentoCalculado = 0;
                }
                else
                {
                    datos.precioCalculado = (decimal)datos.producto.PVP;
                }

                // Si quisiéramos comprobar también las condiciones que tiene en ficha, descomentar la siguiente línea
                // comprobarCondiciones(datos);
            }
        }

        private static void cargarListaCondiciones()
        {
            // Rellenamos la lista estática de las condiciones que queremos comprobar.
            // Se implenta de esta forma para que sea sencillo en el futuro poner o 
            // o quitar condiciones, ya que se implementa como clases individuales que
            // implementan el interfaz ICondicionPrecioDescuento
            listaCondiciones.Add(new OtrosAparatosNoPuedeLlevarDescuento());
            listaCondiciones.Add(new DuNoPuedeLlevarCualquierDescuento());
            listaCondiciones.Add(new RamasonNoPuedeLlevarNingunDescuento());
            listaCondiciones.Add(new CerasConPrecioMinimo());
            listaCondiciones.Add(new ThuyaNoPuedeLlevarCualquierDescuento());
        }

        public static bool comprobarCondiciones(PrecioDescuentoProducto datos) {
            // Primero miramos si ese precio está en las oferta (tiene metidos los precios generales para todos los clientes)
            using (NVEntities db = new NVEntities())
            {
                DescuentosProducto dtoProducto = db.DescuentosProductoes.OrderByDescending(d => d.CantidadMínima).FirstOrDefault(d => d.Empresa == datos.producto.Empresa && d.Nº_Producto == datos.producto.Número && d.CantidadMínima <= datos.cantidad && d.Nº_Cliente == null && d.NºProveedor == null);
                if (dtoProducto != null && dtoProducto.Precio * (1 - dtoProducto.Descuento) <= datos.producto.PVP * (1 - datos.descuentoReal))
                {
                    return true;
                }
            }

            // Recorre listaCondiciones y mira una a una si "datos" cumplen todas las condiciones
            // Devuelve true si cumple todas y false si alguna no se cumple
            // Las que no se cumplen son corregidas durante la comprobación
            if (listaCondiciones == null || listaCondiciones.Count == 0)
            {
                cargarListaCondiciones();
            }

            bool cumpleTodasLasCondiciones = true;

            // Cambiar el for por un while para mejorar rendimiento
            foreach (ICondicionPrecioDescuento condicion in listaCondiciones) {
                cumpleTodasLasCondiciones = condicion.precioAceptado(datos) && cumpleTodasLasCondiciones;
            };
            return cumpleTodasLasCondiciones;
        }

        public static List<ICondicionPrecioDescuento> listaCondiciones = new List<ICondicionPrecioDescuento>();
    }

    public class PrecioDescuentoProducto
    {
        public decimal precioCalculado;
        public decimal descuentoCalculado;
        public Producto producto;
        public string cliente;
        public string contacto;
        public short cantidad;
        public short cantidadOferta;
        public bool aplicarDescuento;
        public string motivo; // cadena para mostrar en la interfaz de usuario
        public decimal? descuentoReal {
            get
            {
                decimal dividendo = (precioCalculado * (1 -descuentoCalculado)  * cantidad);
                decimal divisor = ((decimal)producto.PVP * (cantidad + cantidadOferta));
                return 1 - ( dividendo / divisor );
            }
        }
        public decimal precioCalculadoDeFicha
        {
            get
            {
                decimal? precioProducto = producto.PVP;
                decimal? descuentoFinal = 1 - descuentoReal;
                double baseRedondear = (double)(precioProducto * descuentoFinal);
                return (decimal)Math.Round(baseRedondear, 2);
            }

        }
    }

    public interface ICondicionPrecioDescuento 
    {
        // Pasamos datos del producto para ver qué precio mínimo se le puede dejar.
        // En caso de ser correctos todos los datos que hemos pasado, el procedimiento
        // devolverá true sin modificar "precio", y en caso de no poder ser esas condiciones
        // devolverá false y se modificarán los campos de "precio" que sea necesario para
        // que se pueda producir la venta cumpliendo nuestras condiciones.
        bool precioAceptado(PrecioDescuentoProducto precio);
    }

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
    public class DuNoPuedeLlevarCualquierDescuento : ICondicionPrecioDescuento
    {
        public bool precioAceptado(PrecioDescuentoProducto precio)
        {
            // Los productos de la familia Du no pueden tener más de un 15% de descuento
            if (precio.producto.Familia.ToLower().Trim() == "du" && precio.descuentoReal > (decimal).15)
            {
                precio.precioCalculado = (decimal)precio.producto.PVP;
                precio.descuentoCalculado = (decimal).15;
                precio.motivo = "No se puede hacer un descuento superior al 15% en Du";
                return false;
            }
            else
            {
                return true;
            }
        }
    }
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
