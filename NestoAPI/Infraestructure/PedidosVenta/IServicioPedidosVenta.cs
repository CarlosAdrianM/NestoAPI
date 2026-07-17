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
        CentrosCoste CalcularCentroCoste(string empresa, string vendedor);
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
        // NestoAPI#277: vendedor base del cliente (ficha) y sus vendedores por grupo, para calcular el
        // vendedor de las líneas de cuenta contable (portes/reembolso) cuando el DTO no lo trae.
        string LeerVendedorCliente(string empresa, string cliente, string contacto);
        List<VendedorGrupoProductoDTO> LeerVendedoresClienteGrupo(string empresa, string cliente, string contacto);
        // NestoAPI#249: grupos alternativos por los que puede comisionar un producto marcado
        // (tabla ProductosGruposComisionablesAlternativos) y vendedor asociado a un usuario
        // (ParametrosUsuario, clave "Vendedor"; null si no tiene).
        List<string> LeerGruposComisionablesAlternativos(string empresa, string producto);
        string LeerVendedorDeUsuario(string empresa, string usuario);
        // NestoAPI#319: subgrupo EXISTENTE para una línea cuyo grupo se ha convertido (#249). Nunca
        // inventa: devuelve el subgrupo por defecto del grupo (convención código = grupo: PEL/PEL,
        // ACC/ACC...) o, si no existe, el primero del grupo por orden; null si el grupo no tiene
        // ninguno. Así la conversión jamás rompe FK_LinPedidoVta_SubGruposProducto.
        string LeerSubGrupoParaGrupo(string empresa, string grupo);
        string LeerTipoExclusiva(string empresa, string producto);
        List<LinPedidoVta> CargarLineasPedidoPendientes(int pedido);
        List<LinPedidoVta> CargarLineasPedidoSinPicking(int pedido);
        List<EfectoPedidoVenta> CargarEfectosPedido(string empresa, int pedido);
    }
}
