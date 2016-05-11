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
            if (!aplicarDescuento || familia == "Ramason" || subGrupo == "ACP")
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

                comprobarCondiciones(datos);
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
        }

        public static bool comprobarCondiciones(PrecioDescuentoProducto datos) {
            // Recorre listaCondiciones y mira una a una si "datos" cumplen todas las condiciones
            // Devuelve true si cumple todas y false si alguna no se cumple
            // Las que no se cumplen son corregidas durante la comprobación

            if (listaCondiciones == null || listaCondiciones.Count == 0)
            {
                cargarListaCondiciones();
            }

            bool cumpleTodasLasCondiciones = true;
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
    public class DuNoPuedeLlevarCualquierDescuento : ICondicionPrecioDescuento
    {
        public bool precioAceptado(PrecioDescuentoProducto precio)
        {
            // Los productos de la familia Du no pueden tener más de un 15% de descuento

            if (precio.producto.Familia.Trim() == "Du" && precio.descuentoReal > (decimal).15)
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

            if (precio.aplicarDescuento && precio.producto.SubGrupo == "ACP")
            {
                precio.aplicarDescuento = false;
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

            if (precio.aplicarDescuento && precio.producto.Familia == "Ramason")
            {
                precio.aplicarDescuento = false;
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
