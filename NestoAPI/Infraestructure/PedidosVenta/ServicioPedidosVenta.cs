using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    public class ServicioPedidosVenta : IServicioPedidosVenta
    {
        private readonly NVEntities db = new NVEntities();

        public string CalcularAlmacen(string usuario, string empresa, int numeroPedido)
        {
            using (NVEntities db = new NVEntities())
            {
                string almacen = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == empresa && l.Número == numeroPedido)?.Almacén;
                if (!string.IsNullOrWhiteSpace(almacen))
                {
                    return almacen;
                }
                ParametroUsuario parametroUsuario;

                if (usuario != null)
                {
                    parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == empresa && p.Usuario == usuario && p.Clave == "AlmacénPedidoVta");
                    if (parametroUsuario != null && !string.IsNullOrWhiteSpace(parametroUsuario.Valor))
                    {
                        return parametroUsuario.Valor.Trim();
                    }
                }

                return Constantes.Productos.ALMACEN_POR_DEFECTO;
            }
        }

        public CentrosCoste CalcularCentroCoste(string empresa, int numeroPedido)
        {
            CabPedidoVta cabPedidoCoste = db.CabPedidoVtas.FirstOrDefault(l => l.Empresa == empresa && l.Número == numeroPedido);
            if (cabPedidoCoste == null)
            {
                cabPedidoCoste = db.CabPedidoVtas.Local.FirstOrDefault(l => l.Empresa == empresa && l.Número == numeroPedido);
            }
            string vendedor = cabPedidoCoste?.Vendedor;
            return string.IsNullOrWhiteSpace(vendedor)
                ? throw new Exception("No se puede calcular el centro de coste del pedido " + numeroPedido.ToString() + ", porque falta el vendedor")
                : CalcularCentroCoste(empresa, vendedor);
        }

        public CentrosCoste CalcularCentroCoste(string empresa, string vendedor)
        {
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                throw new Exception("No se puede calcular el centro de coste porque falta el vendedor");
            }
            ParametroUsuario parametroUsuario;
            UsuarioVendedor usuarioVendedor = db.UsuarioVendedores.SingleOrDefault(u => u.Vendedor == vendedor);

            if (usuarioVendedor == null)
            {
                throw new Exception("El vendedor " + vendedor + " no tiene usuario");
            }

            string usuario = usuarioVendedor.Usuario;
            string numeroCentroCoste = string.Empty;
            if (!string.IsNullOrWhiteSpace(usuario))
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == empresa && p.Usuario == usuario && p.Clave == "CentroCosteDefecto");
                if (parametroUsuario != null && !string.IsNullOrWhiteSpace(parametroUsuario.Valor))
                {
                    numeroCentroCoste = parametroUsuario.Valor.Trim();
                }
            }
            else
            {
                throw new Exception("No se puede calcular el centro de coste porque el vendedor " + vendedor + " no tiene un usuario asociado");
            }

            return db.CentrosCostes.SingleOrDefault(c => c.Empresa == empresa && c.Número == numeroCentroCoste);
        }

        public string CalcularDelegacion(string usuario, string empresa, int numeroPedido)
        {
            string delegacion = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == empresa && l.Número == numeroPedido)?.Delegación;
            if (!string.IsNullOrWhiteSpace(delegacion))
            {
                return delegacion;
            }
            ParametroUsuario parametroUsuario;

            if (usuario != null)
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == empresa && p.Usuario == usuario && p.Clave == "DelegaciónDefecto");
                if (parametroUsuario != null && !string.IsNullOrWhiteSpace(parametroUsuario.Valor))
                {
                    return parametroUsuario.Valor.Trim();
                }
            }

            return Constantes.Empresas.DELEGACION_POR_DEFECTO;
        }

        public string CalcularFormaVenta(string usuario, string empresa, int numeroPedido)
        {
            string formaVenta = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == empresa && l.Número == numeroPedido)?.Forma_Venta;
            if (!string.IsNullOrWhiteSpace(formaVenta))
            {
                return formaVenta;
            }
            ParametroUsuario parametroUsuario;

            if (usuario != null)
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == empresa && p.Usuario == usuario && p.Clave == "FormaVentaDefecto");
                if (parametroUsuario != null && !string.IsNullOrWhiteSpace(parametroUsuario.Valor))
                {
                    return parametroUsuario.Valor.Trim();
                }
            }

            return Constantes.Empresas.FORMA_VENTA_POR_DEFECTO;
        }

        public bool EsSobrePedido(string producto, short cantidad)
        {
            Producto productoBuscado = LeerProducto(Constantes.Empresas.EMPRESA_POR_DEFECTO, producto);
            if (productoBuscado.Estado == Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO)
            {
                return false;
            }

            // Este db hay que refactorizarlo, porque no debería ser obligatorio
            using (NVEntities db = new NVEntities())
            {
                ProductoPlantillaDTO productoNuevo = new ProductoPlantillaDTO(producto, db);
                return productoNuevo.CantidadDisponible() < cantidad;
            }
        }

        public CabPedidoVta LeerCabPedidoVta(string empresa, int pedido)
        {
            return db.CabPedidoVtas.SingleOrDefault(c => c.Empresa == empresa && c.Número == pedido);
        }

        public DescuentosCliente LeerDescuentoCliente(string empresa, string cliente, string contacto)
        {
            return db.DescuentosClientes.OrderBy(d => d.ImporteMínimo).FirstOrDefault(d => d.Empresa == empresa && d.Nº_Cliente == cliente && d.Contacto == contacto);
        }

        public Empresa LeerEmpresa(string empresa)
        {
            return db.Empresas.SingleOrDefault(e => e.Número == empresa);
        }

        public Inmovilizado LeerInmovilizado(string empresa, string inmovilizado)
        {
            return db.Inmovilizados.Single(p => p.Empresa == empresa && p.Número == inmovilizado);
        }

        public ParametroIVA LeerParametroIVA(string empresa, string ivaClienteProveedor, string ivaProducto)
        {
            return db.ParametrosIVA.SingleOrDefault(p => p.Empresa == empresa && p.IVA_Cliente_Prov == ivaClienteProveedor && p.IVA_Producto == ivaProducto);
        }

        public PlanCuenta LeerPlanCuenta(string empresa, string cuenta)
        {
            return db.PlanCuentas.SingleOrDefault(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta);
        }

        public PlazoPago LeerPlazosPago(string empresa, string plazosPago)
        {
            return db.PlazosPago.SingleOrDefault(p => p.Empresa == empresa && p.Número == plazosPago);
        }

        public Producto LeerProducto(string empresa, string producto)
        {
            return db.Productos.SingleOrDefault(p => p.Empresa == empresa && p.Número == producto);
        }

        public string LeerTipoExclusiva(string empresa, string numeroProducto)
        {
            // la cláusula include es case sensitive, por lo que la familia "pure" es distinta a "Pure" y no la encuentra (es lo que da error)
            Producto producto = db.Productos.Include(f => f.Familia1).Where(p => p.Empresa == empresa && p.Número == numeroProducto).SingleOrDefault();
            return producto.Familia1.TipoExclusiva;
        }

        internal List<LinPedidoVta> CargarLineasPedidoPendientes(int pedido)
        {
            using (var db = new NVEntities())
            {
                return db.LinPedidoVtas
                    .Where(l => l.Número == pedido && l.Estado == Constantes.EstadosLineaVenta.PENDIENTE)
                    .ToList();
            }
        }

        internal List<LinPedidoVta> CargarLineasPedidoSinPicking(int pedido)
        {
            using (var db = new NVEntities())
            {
                return db.LinPedidoVtas
                        .Where(l => l.Número == pedido && l.Picking != 0 && l.Estado == Constantes.EstadosLineaVenta.EN_CURSO)
                        .ToList();
            }
        }
    }
}