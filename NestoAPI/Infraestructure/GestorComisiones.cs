using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Infraestructure
{
    public class GestorComisiones
    {
        public static void ActualizarVendedorPedidoGrupoProducto(NVEntities db, CabPedidoVta cabPedido, PedidoVentaDTO pedido)
        {
            ICollection<VendedorPedidoGrupoProducto> vendedoresActuales = db.VendedoresPedidosGruposProductos.Where(v => v.Empresa == cabPedido.Empresa && v.Pedido == cabPedido.Número).ToList();
            if (vendedoresActuales == null)
            {
                return;
            }

            VendedorPedidoGrupoProducto vendedorPedidoGrupoActual = vendedoresActuales.FirstOrDefault();
            VendedorGrupoProductoDTO vendedorPedidoGrupoNuevo = pedido.VendedoresGrupoProducto.FirstOrDefault();
            if (vendedorPedidoGrupoActual != null)
            {
                vendedorPedidoGrupoActual.Vendedor = vendedorPedidoGrupoNuevo.vendedor;
            } else if (vendedorPedidoGrupoNuevo != null)
            {
                vendedorPedidoGrupoActual = new VendedorPedidoGrupoProducto{
                    Empresa = cabPedido.Empresa,
                    Pedido = cabPedido.Número,
                    GrupoProducto = vendedorPedidoGrupoNuevo.grupoProducto,
                    Vendedor = vendedorPedidoGrupoNuevo.vendedor,
                    Usuario = pedido.usuario
                };
                db.VendedoresPedidosGruposProductos.Add(vendedorPedidoGrupoActual);
            }            
        }

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

        internal static void ActualizarVendedorClienteGrupoProducto(NVEntities db, Cliente clienteDB, ClienteDTO cliente)
        {
            ICollection<VendedorClienteGrupoProducto> vendedoresActuales = db.VendedoresClientesGruposProductos.Where(v => v.Empresa == clienteDB.Empresa && v.Cliente == clienteDB.Nº_Cliente && v.Contacto == clienteDB.Contacto).ToList();
            if (vendedoresActuales == null)
            {
                return;
            }

            VendedorClienteGrupoProducto vendedorGrupoActual = vendedoresActuales.FirstOrDefault();
            VendedorGrupoProductoDTO vendedorGrupoNuevo = cliente.VendedoresGrupoProducto.FirstOrDefault();
            if (vendedorGrupoActual != null)
            {
                if (vendedorGrupoActual.Vendedor != vendedorGrupoNuevo.vendedor)
                {
                    vendedorGrupoActual.Usuario = cliente.usuario;
                } else
                {
                    vendedorGrupoActual.Usuario = vendedorGrupoNuevo.usuario;
                }
                vendedorGrupoActual.Vendedor = vendedorGrupoNuevo.vendedor;
                vendedorGrupoActual.Estado = vendedorGrupoNuevo.estado;
            }
            else if (vendedorGrupoNuevo != null && vendedorGrupoNuevo.vendedor != null)
            {
                vendedorGrupoActual = new VendedorClienteGrupoProducto
                {
                    Empresa = clienteDB.Empresa,
                    Cliente = clienteDB.Nº_Cliente,
                    Contacto = clienteDB.Contacto,
                    GrupoProducto = vendedorGrupoNuevo.grupoProducto,
                    Vendedor = vendedorGrupoNuevo.vendedor,
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = cliente.usuario
                };
                db.VendedoresClientesGruposProductos.Add(vendedorGrupoActual);
            }
        }
    }
}