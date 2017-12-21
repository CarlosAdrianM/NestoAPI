using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure
{
    public class ServicioPrecios : IServicioPrecios
    {
        public Producto BuscarProducto(string producto)
        {
            using (NVEntities db = new NVEntities())
            {
                if (producto == null || producto.Trim() == "")
                {
                    return null;
                }
                /*
                Producto borrarlo = db.Productos.SingleOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto.Trim());
                if (borrarlo == null)
                {
                    throw new Exception("Este código hay que borrarlo y descomentar le return de abajo");
                } else
                {
                    return borrarlo;
                }
                */

                return db.Productos.Single(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto.Trim());
            }
        }

        public List<OfertaPermitida> BuscarOfertasPermitidas(string numeroProducto)
        {
            using (NVEntities db = new NVEntities())
            {
                if (numeroProducto == null || numeroProducto.Trim() == "")
                {
                    return null;
                }

                Producto producto = db.Productos.SingleOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == numeroProducto);

                if (producto == null)
                {
                    return null;
                }

                return db.OfertasPermitidas.Where(o => o.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && (o.Número == numeroProducto.Trim() || o.Familia == producto.Familia)).ToList();
            }
        }

        public List<DescuentosProducto> BuscarDescuentosPermitidos(string numeroProducto, string numeroCliente, string contactoCliente)
        {
            using (NVEntities db = new NVEntities())
            {
                if (numeroProducto == null || numeroProducto.Trim() == "")
                {
                    return null;
                }

                Producto producto = db.Productos.SingleOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == numeroProducto);

                if (producto == null)
                {
                    return null;
                }

                return db.DescuentosProductoes.Where(d => d.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && 
                    (d.Nº_Producto == numeroProducto.Trim() || d.Familia == producto.Familia || d.GrupoProducto == producto.Grupo) &&
                    (d.Nº_Cliente == null || d.Nº_Cliente == numeroCliente) &&
                    (d.Contacto == null || (d.Nº_Cliente == numeroCliente && d.Contacto == contactoCliente))
                    ).ToList();
            }
        }
    }
}