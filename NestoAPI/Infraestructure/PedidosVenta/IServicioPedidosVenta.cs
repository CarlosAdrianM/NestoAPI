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
        CentrosCoste CalcularCentroCoste(string empresa, string vendedor);
        CentrosCoste LeerCentroCoste(string empresa, string numero);
        string CalcularDelegacion(string usuario, string empresa, int numeroPedido);
        string CalcularFormaVenta(string usuario, string empresa, int numeroPedido);
        bool EsSobrePedido(string producto, short cantidad);
        CabPedidoVta LeerCabPedidoVta(string empresa, int pedido);
        DescuentosCliente LeerDescuentoCliente(string empresa, string cliente, string contacto);
        /// <summary>NestoAPI#764: el código postal de entrega del pedido (el del contacto en Clientes), recortado; null si no está.</summary>
        string LeerCodigoPostalEntrega(string empresa, string cliente, string contacto);
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
        /// <summary>NestoAPI#686: líneas del pedido en albarán y aún sin facturar.</summary>
        List<LinPedidoVta> CargarLineasAlbaranSinFacturar(int pedido);
        /// <summary>
        /// NestoAPI#686: lo que ya se ha cobrado (o se va a cobrar) contra reembolso en envíos del pedido desde esa fecha:
        /// los que ya han salido (tramitados, entregados, incidentados o cerrados a mano; no los devueltos ni el que está
        /// en curso, que es el de la etiqueta que se está haciendo).
        /// </summary>
        decimal ReembolsosCobradosDelPedido(string empresa, int pedido, DateTime desde);
        List<EfectoPedidoVenta> CargarEfectosPedido(string empresa, int pedido);
        /// <summary>NestoAPI#569: los prepagos del pedido (facturados o no).</summary>
        List<Prepago> CargarPrepagosPedido(string empresa, int pedido);
        /// <summary>NestoAPI#679: los enlaces de pago (TPV virtual) pendientes o autorizados del pedido (por el concepto).</summary>
        List<Models.Pagos.EnlacePagoPedido> CargarEnlacesPagoPedido(string empresa, int pedido);
        /// <summary>NestoAPI#513: números de factura (distintos, recortados) de las líneas del pedido; vacío si no está facturado.</summary>
        List<string> FacturasDelPedido(string empresa, int pedido);
        /// <summary>
        /// NestoAPI#513: lo que queda pendiente de cobrar EN EFECTIVO de esas facturas en el extracto del
        /// cliente (efectos con FormaPago EFC, suma de ImportePdte). Es el reembolso real que falta.
        /// </summary>
        decimal PendienteEfectivoDeFacturas(string empresa, string cliente, IEnumerable<string> facturas);
    }
}
