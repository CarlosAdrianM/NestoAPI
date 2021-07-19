using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    public interface IServicioPedidosVenta
    {
        string CalcularAlmacen(string usuario, string empresa, int numeroPedido);
        CentrosCoste CalcularCentroCoste(string empresa, int numeroPedido);
        string CalcularDelegacion(string usuario, string empresa, int numeroPedido);
        string CalcularFormaVenta(string usuario, string empresa, int numeroPedido);
        bool EsSobrePedido(string producto, short cantidad);
        CabPedidoVta LeerCabPedidoVta(string empresa, int pedido);
        DescuentosCliente LeerDescuentoCliente(string empresa, string cliente, string contacto);
        Empresa LeerEmpresa(string empresa);
        Inmovilizado LeerInmovilizado(string empresa, string inmovilizado);
        ParametroIVA LeerParametroIVA(string empresa, string ivaClienteProveedor, string ivaProducto);
        PlanCuenta LeerPlanCuenta(string empresa, string cuenta);
        PlazoPago LeerPlazosPago(string empresa, string plazosPago);
        Producto LeerProducto(string empresa, string producto);
        string LeerTipoExclusiva(string empresa, string producto);
    }
}
