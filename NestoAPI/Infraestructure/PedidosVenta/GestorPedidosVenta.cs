using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    public class GestorPedidosVenta
    {
        private ServicioPedidosVenta servicio;
        public GestorPedidosVenta(ServicioPedidosVenta servicio)
        {
            this.servicio = servicio;
        }

        internal string CalcularAlmacen(string usuario, string empresa, int numeroPedido)
        {
            return servicio.CalcularAlmacen(usuario, empresa, numeroPedido);
        }
        internal CentrosCoste CalcularCentroCoste(string empresa, int numeroPedido)
        {
            return servicio.CalcularCentroCoste(empresa, numeroPedido);
        }
        internal string CalcularDelegacion(string usuario, string empresa, int numeroPedido)
        {
            return servicio.CalcularDelegacion(usuario, empresa, numeroPedido);
        }
        internal string CalcularFormaVenta(string usuario, string empresa, int numeroPedido)
        {
            return servicio.CalcularFormaVenta(usuario, empresa, numeroPedido);
        }
        internal void CalcularDescuentoTodasLasLineas(List<LinPedidoVta> lineas, decimal descuento)
        {
            foreach (LinPedidoVta linea in lineas)
            {
                linea.Descuento = descuento;
                CalcularImportesLinea(linea);
            }
        }
        // Si pongo public, lo confunde con el método POST, porque solo llevan un parámetro
        public void CalcularImportesLinea(LinPedidoVta linea)
        {
            string iva = servicio.LeerCabPedidoVta(linea.Empresa, linea.Número).IVA;
            CalcularImportesLinea(linea, iva);
        }

        public void CalcularImportesLinea(LinPedidoVta linea, string iva)
        {
            decimal baseImponible, bruto, importeDescuento, importeIVA, importeRE, sumaDescuentos, porcentajeRE;
            byte porcentajeIVA;
            ParametroIVA parametroIva;

            // Para que redondee a la baja
            bruto = (decimal)(linea.Cantidad * (decimal.Truncate((decimal)linea.Precio * 10000) / 10000));
            if (linea.Aplicar_Dto)
            {
                sumaDescuentos = (1 - (1 - (linea.DescuentoCliente)) * (1 - (linea.DescuentoProducto)) * (1 - (linea.Descuento)) * (1 - (linea.DescuentoPP)));
            }
            else
            {
                linea.DescuentoProducto = 0;
                sumaDescuentos = (1 - (1 - (linea.Descuento)) * (1 - (linea.DescuentoPP)));
            }
            importeDescuento = Math.Round(bruto * sumaDescuentos, 2, MidpointRounding.AwayFromZero);
            baseImponible = Math.Round(bruto - importeDescuento, 2, MidpointRounding.AwayFromZero);
            if (!string.IsNullOrWhiteSpace(iva))
            {
                parametroIva = servicio.LeerParametroIVA(linea.Empresa, iva, linea.IVA);
                porcentajeIVA = parametroIva != null ? (byte)parametroIva.C__IVA : (byte)0;
                porcentajeRE = parametroIva != null ? (decimal)parametroIva.C__RE / 100 : (decimal)0;
                importeIVA = baseImponible * porcentajeIVA / 100;
                importeRE = baseImponible * porcentajeRE;
            }
            else
            {
                porcentajeIVA = 0;
                porcentajeRE = 0;
                importeIVA = 0;
                importeRE = 0;
            }

            // Ponemos los valores en la línea
            linea.Bruto = bruto;
            linea.Base_Imponible = baseImponible;
            linea.Total = baseImponible + importeIVA + importeRE;
            linea.PorcentajeIVA = porcentajeIVA;
            linea.PorcentajeRE = porcentajeRE;
            linea.SumaDescuentos = sumaDescuentos;
            linea.ImporteDto = importeDescuento;
            linea.ImporteIVA = importeIVA;
            linea.ImporteRE = importeRE;
        }
        public LinPedidoVta CrearLineaVta(string empresa, int numeroPedido, byte tipoLinea, string producto, short cantidad, decimal precio, string usuario)
        {
            string delegacion = CalcularDelegacion(usuario, empresa, numeroPedido);
            string almacen = CalcularAlmacen(usuario, empresa, numeroPedido);
            string formaVenta = CalcularFormaVenta(usuario, empresa, numeroPedido);

            string texto;
            switch (tipoLinea)
            {
                case Constantes.TiposLineaVenta.CUENTA_CONTABLE:
                    texto = servicio.LeerPlanCuenta(empresa, producto).Concepto;
                    texto = texto.Substring(0, 50);
                    break;
                case Constantes.TiposLineaVenta.PRODUCTO:
                    texto = servicio.LeerProducto(empresa, producto).Nombre;
                    break;
                case Constantes.TiposLineaVenta.INMOVILIZADO:
                    texto = servicio.LeerInmovilizado(empresa, producto).Descripción;
                    break;
                default:
                    texto = string.Empty;
                    break;
            }

            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = tipoLinea,
                estado = Constantes.EstadosLineaVenta.EN_CURSO,
                producto = producto,
                texto = texto,
                cantidad = cantidad,
                precio = precio,
                delegacion = delegacion,
                formaVenta = formaVenta,
                almacen = almacen,
                usuario = string.IsNullOrWhiteSpace(usuario) ? System.Environment.UserDomainName + "\\" + System.Environment.UserName : System.Environment.UserDomainName + "\\" + usuario
            };
            return CrearLineaVta(linea, empresa, numeroPedido);
        }

        public LinPedidoVta CrearLineaVta(LineaPedidoVentaDTO linea, string empresa, int numeroPedido)
        {
            // Si hubiese en dos empresas el mismo pedido, va a dar error
            CabPedidoVta pedido = servicio.LeerCabPedidoVta(empresa, numeroPedido);
            PlazoPago plazo = servicio.LeerPlazosPago(pedido.Empresa, pedido.PlazosPago);
            if (pedido.IVA != null && linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
            {
                Producto producto = servicio.LeerProducto(empresa, linea.producto);
                linea.iva = producto.IVA_Repercutido;
            }
            else if (pedido.IVA != null && linea.tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE)
            {
                PlanCuenta cuenta = servicio.LeerPlanCuenta(empresa, linea.producto);
                linea.iva = cuenta.IVA;
            }
            return CrearLineaVta(linea, numeroPedido, pedido.Empresa, pedido.IVA, plazo, pedido.Nº_Cliente, pedido.Contacto, pedido.Ruta);
        }
        public LinPedidoVta CrearLineaVta(LineaPedidoVentaDTO linea, int numeroPedido, string empresa, string iva, PlazoPago plazoPago, string cliente, string contacto)
        {
            CabPedidoVta pedido = servicio.LeerCabPedidoVta(empresa, numeroPedido);
            return CrearLineaVta(linea, numeroPedido, empresa, iva, plazoPago, cliente, contacto, pedido.Ruta);
        }
        public LinPedidoVta CrearLineaVta(LineaPedidoVentaDTO linea, int numeroPedido, string empresa, string iva, PlazoPago plazoPago, string cliente, string contacto, string ruta)
        {
            string tipoExclusiva, grupo, subGrupo, familia, ivaRepercutido;
            decimal coste, precioTarifa;
            short estadoProducto;
            CentrosCoste centroCoste = null;

            // Calculamos las variables que se pueden corresponden a la cabecera
            decimal descuentoCliente, descuentoPP;
            DescuentosCliente dtoCliente = servicio.LeerDescuentoCliente(empresa, cliente, contacto);
            descuentoCliente = dtoCliente != null ? dtoCliente.Descuento : 0;
            descuentoPP = plazoPago.DtoProntoPago;

            switch (linea.tipoLinea)
            {

                case Constantes.TiposLineaVenta.PRODUCTO:
                    if (linea.cantidad == 0)
                    {
                        throw new Exception("No se pueden crear líneas de producto con cantidad 0");
                    }
                    Producto producto = servicio.LeerProducto(empresa, linea.producto);
                    precioTarifa = (decimal)producto.PVP;
                    coste = (decimal)producto.PrecioMedio;
                    grupo = producto.Grupo;
                    subGrupo = producto.SubGrupo;
                    familia = producto.Familia;
                    ivaRepercutido = producto.IVA_Repercutido; // ¿Se usa? Ojo, que puede venir el IVA nulo y estar bien
                    estadoProducto = (short)producto.Estado;
                    break;

                case Constantes.TiposLineaVenta.CUENTA_CONTABLE:
                    precioTarifa = 0;
                    coste = 0;
                    grupo = null;
                    subGrupo = null;
                    familia = null;
                    ivaRepercutido = servicio.LeerEmpresa(empresa).TipoIvaDefecto;
                    estadoProducto = 0;
                    if (linea.producto.Substring(0, 1) == "6" || linea.producto.Substring(0, 1) == "7")
                    {
                        centroCoste = CalcularCentroCoste(empresa, numeroPedido);
                    }
                    break;

                default:
                    precioTarifa = 0;
                    coste = 0;
                    grupo = null;
                    subGrupo = null;
                    familia = null;
                    ivaRepercutido = servicio.LeerEmpresa(empresa).TipoIvaDefecto;
                    estadoProducto = 0;
                    break;
            }


            // Posiblemente este if se pueda refactorizar con el switch de arriba, pero hay que comprobarlo bien primero
            if (linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
            {
                tipoExclusiva = servicio.LeerTipoExclusiva(empresa, linea.producto);
            }
            else
            {
                tipoExclusiva = null;
            }

            // Calculamos los valores que nos falten
            // esto habría que refactorizarlo para que solo lo lea una vez por pedido
            if (linea.almacen == null)
            {
                linea.almacen = CalcularAlmacen(linea.usuario, empresa, numeroPedido);
            }

            if (linea.formaVenta == null)
            {
                linea.formaVenta = CalcularFormaVenta(linea.usuario, empresa, numeroPedido);
            }

            if (linea.delegacion == null)
            {
                linea.delegacion = CalcularDelegacion(linea.usuario, empresa, numeroPedido);
            }


            LinPedidoVta lineaNueva = new LinPedidoVta
            {
                Estado = linea.estado,
                TipoLinea = linea.tipoLinea,
                Producto = linea.producto,
                Texto = linea.texto.Length > 50 ? linea.texto.Substring(0, 50) : linea.texto, // porque 50 es la longitud del campo
                Cantidad = linea.cantidad,
                Fecha_Entrega = FechaEntregaAjustada(linea.fechaEntrega.Date, ruta),
                Precio = linea.precio,
                PrecioTarifa = precioTarifa,
                Coste = coste,
                Descuento = linea.descuento,
                DescuentoProducto = linea.descuentoProducto,
                Aplicar_Dto = linea.aplicarDescuento,
                VtoBueno = linea.vistoBueno,
                Usuario = linea.usuario,
                Almacén = linea.almacen,
                IVA = linea.iva,
                Grupo = grupo,
                Empresa = empresa,
                Número = numeroPedido,
                Nº_Cliente = cliente,
                Contacto = contacto,
                DescuentoCliente = descuentoCliente,
                DescuentoPP = descuentoPP,
                Delegación = linea.delegacion,
                Forma_Venta = linea.formaVenta,
                SubGrupo = subGrupo,
                Familia = familia,
                TipoExclusiva = tipoExclusiva,
                Picking = 0,
                NºOferta = linea.oferta,
                BlancoParaBorrar = "NestoAPI",
                LineaParcial = linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO ? !EsSobrePedido(linea.producto, linea.cantidad) : true,
                EstadoProducto = estadoProducto,
                CentroCoste = centroCoste?.Número,
                Departamento = centroCoste?.Departamento
            };
            /*
            Nota sobre LineaParcial: aunque siga llamándos el campo línea parcial, por retrocompatibidad, en realidad
            ya nada tiene que ver con que se quede una parte de la línea sin entregar, sino que ahora indica si tiene
            que salir aunque no llegue al mínimo. De este modo, todo lo que sea estado 0, por ejemplo, sale siempre, por 
            lo que la línea parcial siempre será cero (no es sobre pedido).
            */

            CalcularImportesLinea(lineaNueva, iva);

            return lineaNueva;
        }

        private bool EsSobrePedido(string producto, short cantidad)
        {
            return servicio.EsSobrePedido(producto, cantidad);
        }

        // A las 11h de la mañana se cierra la ruta y los pedidos que se metan son ya para el día siguiente
        internal DateTime FechaEntregaAjustada(DateTime fecha, string ruta)
        {
            DateTime fechaMinima;

            if (ruta != Constantes.Pedidos.RUTA_GLOVO && GestorImportesMinimos.esRutaConPortes(ruta))
            {
                fechaMinima = DateTime.Now.Hour < Constantes.Picking.HORA_MAXIMA_AMPLIAR_PEDIDOS ? DateTime.Today : DateTime.Today.AddDays(1);
            }
            else
            {
                fechaMinima = DateTime.Today;
            }

            return fechaMinima < fecha ? fecha : fechaMinima;
        }

        internal static async Task<PedidoVentaDTO> LeerPedido(string empresa, int numero)
        {
            using (NVEntities db = new NVEntities()){
                CabPedidoVta cabPedidoVta = await db.CabPedidoVtas.SingleOrDefaultAsync(c => c.Empresa == empresa && c.Número == numero).ConfigureAwait(false);
                if (cabPedidoVta == null)
                {
                    return null;
                }

                List<LineaPedidoVentaDTO> lineasPedido = db.LinPedidoVtas.Where(l => l.Empresa == empresa && l.Número == numero && l.Estado > -99)
                    .Select(l => new LineaPedidoVentaDTO
                    {
                        id = l.Nº_Orden,
                        almacen = l.Almacén,
                        aplicarDescuento = l.Aplicar_Dto,
                        cantidad = (l.Cantidad != null ? (short)l.Cantidad : (short)0),
                        delegacion = l.Delegación,
                        descuento = l.Descuento,
                        descuentoProducto = l.DescuentoProducto,
                        estado = l.Estado,
                        fechaEntrega = l.Fecha_Entrega,
                        formaVenta = l.Forma_Venta,
                        iva = l.IVA,
                        oferta = l.NºOferta,
                        picking = (l.Picking != null ? (int)l.Picking : 0),
                        precio = (l.Precio != null ? (decimal)l.Precio : 0),
                        producto = l.Producto.Trim(),
                        texto = l.Texto.Trim(),
                        tipoLinea = l.TipoLinea,
                        usuario = l.Usuario,
                        vistoBueno = l.VtoBueno,
                        baseImponible = l.Base_Imponible,
                        importeIva = l.ImporteIVA,
                        total = l.Total
                    })
                    .OrderBy(l => l.id)
                    .ToList();

                List<VendedorGrupoProductoDTO> vendedoresGrupoProductoPedido = db.VendedoresPedidosGruposProductos.Where(v => v.Empresa == empresa && v.Pedido == numero)
                    .Select(v => new VendedorGrupoProductoDTO
                    {
                        vendedor = v.Vendedor,
                        grupoProducto = v.GrupoProducto
                    })
                    .ToList();

                List<PrepagoDTO> prepagos = db.Prepagos.Where(p => p.Empresa == empresa && p.Pedido == numero).
                    Select(p => new PrepagoDTO
                    {
                        Importe = p.Importe,
                        Factura = p.Factura,
                        CuentaContable = p.CuentaContable,
                        ConceptoAdicional = p.ConceptoAdicional
                    })
                    .ToList();

                PedidoVentaDTO pedido;
                try
                {
                    pedido = new PedidoVentaDTO
                    {
                        empresa = cabPedidoVta.Empresa.Trim(),
                        numero = cabPedidoVta.Número,
                        cliente = cabPedidoVta.Nº_Cliente.Trim(),
                        contacto = cabPedidoVta.Contacto.Trim(),
                        fecha = cabPedidoVta.Fecha,
                        formaPago = cabPedidoVta.Forma_Pago,
                        plazosPago = cabPedidoVta.PlazosPago.Trim(),
                        primerVencimiento = cabPedidoVta.Primer_Vencimiento,
                        iva = cabPedidoVta.IVA,
                        vendedor = cabPedidoVta.Vendedor,
                        comentarios = cabPedidoVta.Comentarios,
                        comentarioPicking = cabPedidoVta.ComentarioPicking,
                        periodoFacturacion = cabPedidoVta.Periodo_Facturacion,
                        ruta = cabPedidoVta.Ruta,
                        serie = cabPedidoVta.Serie,
                        ccc = cabPedidoVta.CCC,
                        origen = !string.IsNullOrWhiteSpace(cabPedidoVta.Origen) ? cabPedidoVta.Origen : cabPedidoVta.Empresa,
                        contactoCobro = cabPedidoVta.ContactoCobro,
                        noComisiona = cabPedidoVta.NoComisiona,
                        vistoBuenoPlazosPago = cabPedidoVta.vtoBuenoPlazosPago,
                        mantenerJunto = cabPedidoVta.MantenerJunto,
                        servirJunto = cabPedidoVta.ServirJunto,
                        notaEntrega = cabPedidoVta.NotaEntrega,
                        usuario = cabPedidoVta.Usuario,
                        LineasPedido = lineasPedido,
                        VendedoresGrupoProducto = vendedoresGrupoProductoPedido,
                        Prepagos = prepagos,
                        EsPresupuesto = lineasPedido.Any(c => c.estado == Constantes.EstadosLineaVenta.PRESUPUESTO),
                    };
                }
                catch (Exception ex)
                {
                    throw ex;
                }

                return pedido;
            }
        }
        internal async Task<PedidoVentaDTO> UnirPedidos(string empresa, int numeroPedidoOriginal, int numeroPedidoAmpliacion)
        {
            PedidoVentaDTO pedidoOriginal = await LeerPedido(empresa, numeroPedidoOriginal).ConfigureAwait(false);
            PedidoVentaDTO pedidoAmpliacion = await LeerPedido(empresa, numeroPedidoAmpliacion).ConfigureAwait(false);

            foreach (LineaPedidoVentaDTO linea in pedidoAmpliacion.LineasPedido.Where(l => l.estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.estado <= Constantes.EstadosLineaVenta.EN_CURSO).ToList())
            {
                linea.id = 0;
                pedidoOriginal.LineasPedido.Add(linea);
                pedidoAmpliacion.LineasPedido.Remove(linea);
            }

            using (PedidosVentaController controller = new PedidosVentaController())
            {
                await controller.PutPedidoVenta(pedidoAmpliacion).ConfigureAwait(false);
                await controller.PutPedidoVenta(pedidoOriginal).ConfigureAwait(false);
            }

            return pedidoOriginal;
        }
    }
}