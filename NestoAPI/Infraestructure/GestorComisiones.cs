using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Infraestructure
{
    public class GestorComisiones
    {
        /// <summary>
        /// NestoAPI#249: grupo con el que debe comisionar la línea de un producto MARCADO (con grupos
        /// alternativos en ProductosGruposComisionablesAlternativos), según quién mete el pedido.
        /// Regla simétrica: si quien mete el pedido comisiona por alguno de los grupos candidatos, la
        /// línea se convierte a ese grupo, SALVO que el grupo de ficha esté protegido por un vendedor
        /// REAL en los vendedores por grupo del cliente (solo se puede "pisar" un grupo sin registro,
        /// con vendedor en blanco o con el genérico NV). Si el pedido no lo mete ningún vendedor del
        /// cliente (oficina, web, tercer vendedor), se queda el grupo de la ficha.
        /// Función pura para poder testearla sin BD.
        /// </summary>
        /// <param name="grupoFicha">Grupo de la ficha del producto.</param>
        /// <param name="gruposAlternativos">Grupos candidatos del producto marcado (sin incluir el de ficha).</param>
        /// <param name="vendedoresPorGrupo">Vendedor por grupo del cliente (clave: grupo con Trim). El valor
        /// puede ser NV: el registro existe pero ni protege ni reclama.</param>
        /// <param name="vendedorCabecera">Vendedor base del pedido (CabPedidoVta, existe siempre).</param>
        /// <param name="vendedorUsuario">Vendedor asociado al usuario que mete el pedido (null si no tiene).</param>
        internal static string ResolverGrupoComisionable(
            string grupoFicha,
            ICollection<string> gruposAlternativos,
            IDictionary<string, string> vendedoresPorGrupo,
            string vendedorCabecera,
            string vendedorUsuario)
        {
            if (string.IsNullOrWhiteSpace(grupoFicha) || gruposAlternativos == null || gruposAlternativos.Count == 0
                || string.IsNullOrWhiteSpace(vendedorUsuario))
            {
                return grupoFicha;
            }

            string usuario = vendedorUsuario.Trim();
            // NV nunca "reclama" líneas: un usuario de oficina con vendedor NV no debe arrastrar el
            // grupo hacia un registro NV del cliente.
            if (usuario.Equals(Constantes.Vendedores.VENDEDOR_GENERAL, StringComparison.OrdinalIgnoreCase))
            {
                return grupoFicha;
            }

            string cabecera = vendedorCabecera?.Trim();
            string ficha = grupoFicha.Trim();

            // Comisionista real de un grupo: su registro (aunque sea NV, que es quien comisionaría)
            // o, si no hay registro o está en blanco, el vendedor de cabecera (existe siempre).
            string ComisionistaReal(string grupo)
            {
                return vendedoresPorGrupo != null
                    && vendedoresPorGrupo.TryGetValue(grupo, out string v)
                    && !string.IsNullOrWhiteSpace(v)
                    ? v.Trim()
                    : cabecera;
            }

            // Si el grupo de ficha ya comisiona a quien mete el pedido, no hay nada que convertir.
            if (string.Equals(ComisionistaReal(ficha), usuario, StringComparison.OrdinalIgnoreCase))
            {
                return grupoFicha;
            }

            // Protección del origen: con un vendedor REAL (ni blanco ni NV) en el grupo de ficha, la
            // línea no se le puede quitar ("ambos pueden pisar únicamente a NV").
            bool fichaProtegida = vendedoresPorGrupo != null
                && vendedoresPorGrupo.TryGetValue(ficha, out string vendedorFicha)
                && !string.IsNullOrWhiteSpace(vendedorFicha)
                && !vendedorFicha.Trim().Equals(Constantes.Vendedores.VENDEDOR_GENERAL, StringComparison.OrdinalIgnoreCase);
            if (fichaProtegida)
            {
                return grupoFicha;
            }

            // Primer grupo candidato por el que comisiona quien mete el pedido. El desempate lo da el
            // orden de la colección: el servicio los entrega en orden ALFABÉTICO (determinista).
            foreach (string alternativo in gruposAlternativos)
            {
                if (string.IsNullOrWhiteSpace(alternativo))
                {
                    continue;
                }
                string candidato = alternativo.Trim();
                if (candidato.Equals(ficha, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.Equals(ComisionistaReal(candidato), usuario, StringComparison.OrdinalIgnoreCase))
                {
                    return candidato;
                }
            }

            return grupoFicha;
        }

        public static void ActualizarVendedorPedidoGrupoProducto(NVEntities db, CabPedidoVta cabPedido, PedidoVentaDTO pedido)
        {
            ICollection<VendedorPedidoGrupoProducto> vendedoresActuales = db.VendedoresPedidosGruposProductos.Where(v => v.Empresa == cabPedido.Empresa && v.Pedido == cabPedido.Número).ToList();
            if (vendedoresActuales == null)
            {
                return;
            }

            VendedorPedidoGrupoProducto vendedorPedidoGrupoActual = vendedoresActuales.FirstOrDefault();
            // #305: los clientes mandan VendedoresGrupoProducto a null (o vacío) habitualmente:
            // no gestionan vendedor por grupo y no deben pisar el registro actual (antes NRE).
            VendedorGrupoProductoDTO vendedorPedidoGrupoNuevo = pedido.VendedoresGrupoProducto?.FirstOrDefault();
            if (vendedorPedidoGrupoNuevo == null)
            {
                return;
            }
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
                    Usuario = pedido.Usuario
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
                    Usuario = pedido.Usuario
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