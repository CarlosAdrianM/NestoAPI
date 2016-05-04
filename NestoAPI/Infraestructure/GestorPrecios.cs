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
        public static void calcularDescuentoProducto(PrecioDescuentoProducto datos)
        {
            DescuentosProducto dtoProducto;

            datos.descuentoCalculado = 0;
            datos.precioCalculado = (decimal)datos.producto.PVP;

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


                // Si no tiene el aplicar descuento marcado, solo calcula precios especiales, pero no descuentos
                if (!datos.aplicarDescuento)
                {
                    return;
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
            }
        }
    }

    public class PrecioDescuentoProducto
    {
        public decimal precioCalculado;
        public decimal descuentoCalculado;
        public Producto producto;
        public string cliente;
        public string contacto;
        public short cantidad;
        public bool aplicarDescuento;
    }
}
