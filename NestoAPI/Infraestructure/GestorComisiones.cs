using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Infraestructure
{
    public class GestorComisiones
    {
        public static void CrearVendedorPedidoGrupoProducto(NVEntities db,CabPedidoVta cabPedido, PedidoVentaDTO pedido)
        {
            if (cabPedido == null)
            {
                return;
            }

            ICollection<VendedorClienteGrupoProducto> listaVendedoresCliente = db.VendedoresClientesGruposProductos.Where(v => v.Empresa == cabPedido.Empresa && v.Cliente == cabPedido.Nº_Cliente && v.Contacto == cabPedido.Contacto).ToList();
            if (listaVendedoresCliente == null || listaVendedoresCliente.Count == 0)
            {
                return; // no es necesario crear vendedor por grupo de producto
            }

            // Un cliente puede tener muchos vendedores distintos, uno por cada grupo
            foreach(VendedorClienteGrupoProducto vendedorCliente in listaVendedoresCliente)
            {
                VendedorPedidoGrupoProducto vendedorPedido = new VendedorPedidoGrupoProducto
                {
                    Empresa = cabPedido.Empresa,
                    Pedido = cabPedido.Número,
                    GrupoProducto = vendedorCliente.GrupoProducto,
                    Vendedor = vendedorCliente.Vendedor,
                    Usuario = pedido.usuario
                };
                db.VendedoresPedidosGruposProductos.Add(vendedorPedido);
            }

        }
    }
}