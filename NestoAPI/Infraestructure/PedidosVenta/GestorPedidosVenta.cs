using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.PedidosBase;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using System.Web.Http;

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
        internal CentrosCoste CalcularCentroCoste(string empresa, string vendedor)
        {
            return servicio.CalcularCentroCoste(empresa, vendedor);
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
            //bruto = (decimal)(linea.Cantidad * (decimal.Truncate((decimal)linea.Precio * 10000) / 10000));
            // Carlos 09/07/23 ¿por qué tiene que redondear a la baja?
            bruto = (decimal)(linea.Cantidad * linea.Precio);
            if (linea.Aplicar_Dto)
            {
                sumaDescuentos = (1 - (1 - (linea.DescuentoCliente)) * (1 - (linea.DescuentoProducto)) * (1 - (linea.Descuento)) * (1 - (linea.DescuentoPP)));
            }
            else
            {
                linea.DescuentoProducto = 0;
                sumaDescuentos = (1 - (1 - (linea.Descuento)) * (1 - (linea.DescuentoPP)));
            }
            importeDescuento = bruto * sumaDescuentos;
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
                Producto = producto,
                texto = texto,
                Cantidad = cantidad,
                PrecioUnitario = precio,
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
                Producto producto = servicio.LeerProducto(empresa, linea.Producto);
                linea.iva = producto.IVA_Repercutido;
            }
            else if (pedido.IVA != null && linea.tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE)
            {
                PlanCuenta cuenta = servicio.LeerPlanCuenta(empresa, linea.Producto);
                linea.iva = cuenta.IVA;
            }
            return CrearLineaVta(linea, numeroPedido, pedido.Empresa, pedido.IVA, plazo, pedido.Nº_Cliente, pedido.Contacto, pedido.Ruta, pedido.Vendedor);
        }
        public LinPedidoVta CrearLineaVta(LineaPedidoVentaDTO linea, int numeroPedido, string empresa, string iva, PlazoPago plazoPago, string cliente, string contacto)
        {
            CabPedidoVta pedido = servicio.LeerCabPedidoVta(empresa, numeroPedido);
            return CrearLineaVta(linea, numeroPedido, empresa, iva, plazoPago, cliente, contacto, pedido.Ruta, pedido.Vendedor);
        }
        public LinPedidoVta CrearLineaVta(LineaPedidoVentaDTO linea, int numeroPedido, string empresa, string iva, PlazoPago plazoPago, string cliente, string contacto, string ruta, string vendedor)
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
                    if (linea.Cantidad == 0)
                    {
                        throw new Exception("No se pueden crear líneas de producto con cantidad 0");
                    }
                    Producto producto = servicio.LeerProducto(empresa, linea.Producto);
                    if (producto.Estado < Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO)
                    {
                        throw new Exception($"El producto {producto.Número.Trim()} ({producto.Nombre?.Trim()}) está en un estado nulo ({producto.Estado})");
                    }
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
                    if (linea.Producto.Substring(0, 1) == "6" || linea.Producto.Substring(0, 1) == "7")
                    {
                        centroCoste = string.IsNullOrWhiteSpace(vendedor) ? CalcularCentroCoste(empresa, numeroPedido) : CalcularCentroCoste(empresa, vendedor);
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
                tipoExclusiva = servicio.LeerTipoExclusiva(empresa, linea.Producto);
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
                Producto = linea.Producto,
                Texto = linea.texto.Length > 50 ? linea.texto.Substring(0, 50) : linea.texto, // porque 50 es la longitud del campo
                Cantidad = (short)linea.Cantidad,
                Fecha_Entrega = FechaEntregaAjustada(linea.fechaEntrega.Date, ruta),
                Precio = linea.PrecioUnitario,
                PrecioTarifa = precioTarifa,
                Coste = coste,
                Descuento = linea.DescuentoLinea,
                DescuentoProducto = linea.DescuentoProducto,
                Aplicar_Dto = linea.AplicarDescuento,
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
                LineaParcial = linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO ? !EsSobrePedido(linea.Producto, (short)linea.Cantidad) : true,
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

                decimal totalComprobacion = Math.Round(cabPedidoVta.LinPedidoVtas.Sum(l => l.Total), 2, MidpointRounding.AwayFromZero);

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
                        Usuario = cabPedidoVta.Usuario                        
                    };
                }
                catch (Exception ex)
                {
                    throw ex;
                }

                var parametros = db.ParametrosIVA
                    .Where(p => p.Empresa == empresa && p.IVA_Cliente_Prov == pedido.iva)
                    .Select(p => new ParametrosIvaBase
                    {
                        CodigoIvaProducto = p.IVA_Producto.Trim(),
                        PorcentajeIvaProducto = (decimal)p.C__IVA / 100,
                        PorcentajeRecargoEquivalencia = (decimal)p.C__RE / 100
                    });

                pedido.ParametrosIva = await parametros.ToListAsync().ConfigureAwait(false);

                var plazosPago = db.PlazosPago.Single(p => p.Empresa == empresa && p.Número == pedido.plazosPago);
                pedido.DescuentoPP = plazosPago.DtoProntoPago;

                List<LineaPedidoVentaDTO> lineasPedido = db.LinPedidoVtas.Where(l => l.Empresa == empresa && l.Número == numero && l.Estado > -99)
                    .Select(l => new LineaPedidoVentaDTO
                    {
                        id = l.Nº_Orden,
                        almacen = l.Almacén,
                        AplicarDescuento = l.Aplicar_Dto,
                        Cantidad = (l.Cantidad != null ? (short)l.Cantidad : (short)0),
                        delegacion = l.Delegación,
                        DescuentoLinea = l.Descuento,
                        DescuentoProducto = l.DescuentoProducto,
                        estado = l.Estado,
                        fechaEntrega = l.Fecha_Entrega,
                        formaVenta = l.Forma_Venta,
                        GrupoProducto = l.Grupo,
                        iva = l.IVA,
                        oferta = l.NºOferta,
                        picking = (l.Picking != null ? (int)l.Picking : 0),
                        PrecioUnitario = (l.Precio != null ? (decimal)l.Precio : 0),
                        Producto = l.Producto.Trim(),
                        SubgrupoProducto = l.SubGrupo,
                        texto = l.Texto.Trim(),
                        tipoLinea = l.TipoLinea,
                        usuario = l.Usuario,
                        vistoBueno = l.VtoBueno,
                        //BaseImponible = l.Base_Imponible,
                        PorcentajeIva = parametros.Where(p => p.CodigoIvaProducto == l.IVA).FirstOrDefault() != null ? parametros.Where(p => p.CodigoIvaProducto == l.IVA).FirstOrDefault().PorcentajeIvaProducto : 0,
                        PorcentajeRecargoEquivalencia = parametros.Where(p => p.CodigoIvaProducto == l.IVA).FirstOrDefault() != null ? parametros.Where(p => p.CodigoIvaProducto == l.IVA).FirstOrDefault().PorcentajeRecargoEquivalencia : 0,
                        //ImporteIva = l.ImporteIVA,
                        //Total = l.Total,
                        //Bruto = l.Bruto
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

                List<EfectoPedidoVentaDTO> efectos = db.EfectosPedidosVentas.Where(p => p.Empresa == empresa && p.Pedido == numero).
                    Select(p => new EfectoPedidoVentaDTO
                    {
                        Id = p.Id,
                        FechaVencimiento = p.FechaVencimiento,
                        Importe = p.Importe,
                        FormaPago = p.FormaPago,
                        Ccc = p.CCC
                    })
                .ToList();

                foreach (var linea in lineasPedido)
                {
                    linea.Pedido = pedido;
                }
                pedido.Lineas = lineasPedido;
                pedido.VendedoresGrupoProducto = vendedoresGrupoProductoPedido;
                pedido.Prepagos = prepagos;
                pedido.Efectos = efectos;
                pedido.crearEfectosManualmente = efectos.Any();
                pedido.EsPresupuesto = lineasPedido.Any(c => c.estado == Constantes.EstadosLineaVenta.PRESUPUESTO);

                decimal totalPedidoLeido = Math.Round(pedido.Total, 2, MidpointRounding.AwayFromZero);
                if (Math.Abs(totalComprobacion - totalPedidoLeido) > 0.05M)
                {
                    Debug.Print($"No cuadra el total guardado ({totalComprobacion.ToString("c")}) con el del pedido leído {totalPedidoLeido.ToString("c")}");
                }

                return pedido;
            }
        }
        internal async Task<PedidoVentaDTO> UnirPedidos(string empresa, int numeroPedidoOriginal, int numeroPedidoAmpliacion)
        {
            PedidoVentaDTO pedidoOriginal = await LeerPedido(empresa, numeroPedidoOriginal).ConfigureAwait(false);
            PedidoVentaDTO pedidoAmpliacion = await LeerPedido(empresa, numeroPedidoAmpliacion).ConfigureAwait(false);

            try
            {
                return await UnirPedidos(pedidoOriginal, pedidoAmpliacion).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        internal async Task<PedidoVentaDTO> UnirPedidos(string empresa, int numeroPedidoOriginal, PedidoVentaDTO pedidoAmpliacion)
        {
            PedidoVentaDTO pedidoOriginal = await LeerPedido(empresa, numeroPedidoOriginal).ConfigureAwait(false);
            try
            {
                return await UnirPedidos(pedidoOriginal, pedidoAmpliacion).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        internal async Task<PedidoVentaDTO> UnirPedidos(PedidoVentaDTO pedidoOriginal, PedidoVentaDTO pedidoAmpliacion)
        {
            bool originalEsPresupuesto = pedidoOriginal.Lineas.Any(l => l.estado == Constantes.EstadosLineaVenta.PRESUPUESTO);            

            foreach (LineaPedidoVentaDTO linea in pedidoAmpliacion.Lineas.Where(l => l.estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.estado <= Constantes.EstadosLineaVenta.EN_CURSO).ToList())
            {
                linea.id = 0;
                if (originalEsPresupuesto)
                {
                    linea.estado = Constantes.EstadosLineaVenta.PRESUPUESTO;
                }
                pedidoOriginal.Lineas.Add(linea);
                pedidoAmpliacion.Lineas.Remove(linea);
            }

            using (TransactionScope transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            {
                using (PedidosVentaController controller = new PedidosVentaController())
                {
                    try
                    {
                        if (pedidoAmpliacion.numero != 0)
                        {
                            await controller.PutPedidoVenta(pedidoAmpliacion).ConfigureAwait(true);
                        }                        
                        await controller.PutPedidoVenta(pedidoOriginal).ConfigureAwait(true);                        
                        transaction.Complete();
                    }
                    catch (TransactionAbortedException ex)
                    {
                        throw new Exception(ex.Message);
                    }
                    catch (HttpResponseException ex)
                    {
                        throw new Exception(ex.Response.ReasonPhrase);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(ex.Message);
                    }
                }
            }

            return pedidoOriginal;
        }
    }
}