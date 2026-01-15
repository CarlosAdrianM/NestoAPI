using Microsoft.Reporting.WebForms;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas
{
    public class GestorFacturas : IGestorFacturas
    {
        private readonly IServicioFacturas servicio;
        public GestorFacturas()
        {
            servicio = new ServicioFacturas();
        }

        public GestorFacturas(IServicioFacturas servicio)
        {
            this.servicio = servicio;
        }

        public ByteArrayContent FacturaEnPDF(string empresa, string numeroFactura)
        {
            Factura factura = LeerFactura(empresa, numeroFactura);
            List<Factura> facturas = new List<Factura>
            {
                factura
            };
            return FacturasEnPDF(facturas);
        }

        public ByteArrayContent FacturasEnPDF(List<Factura> facturas, bool papelConMembrete = false)
        {

            ReportViewer viewer = new ReportViewer();
            viewer.LocalReport.ReportPath = facturas.FirstOrDefault().RutaInforme;
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Facturas", facturas));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Direcciones", facturas.FirstOrDefault().Direcciones));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("LineasFactura", facturas.FirstOrDefault().Lineas));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Vendedores", facturas.FirstOrDefault().Vendedores));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Vencimientos", facturas.FirstOrDefault().Vencimientos));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("NotasAlPie", facturas.FirstOrDefault().NotasAlPie));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Totales", facturas.FirstOrDefault().Totales));

            viewer.LocalReport.SetParameters(new ReportParameter("PapelConMembrete", papelConMembrete.ToString()));

            viewer.LocalReport.Refresh();
            byte[] bytes = viewer.LocalReport.Render("PDF", null, out _, out _, out _, out _, out _);

            //https://stackoverflow.com/questions/29643043/crystalreports-reportdocument-memory-leak-with-database-connections
            viewer.LocalReport.Dispose();
            viewer.Dispose();

            return new ByteArrayContent(bytes);
        }

        public Factura LeerFactura(string empresa, string numeroFactura)
        {

            CabFacturaVta cabFactura = servicio.CargarCabFactura(empresa, numeroFactura);
            LinPedidoVta primeraLinea = cabFactura.LinPedidoVtas.FirstOrDefault();
            ISerieFactura serieFactura = LeerSerie(cabFactura.Serie);

            CabPedidoVta cabPedido = primeraLinea != null ? servicio.CargarCabPedido(empresa, primeraLinea.Número) : null;
            // IMPORTANTE: Los clientes siempre están en EMPRESA_POR_DEFECTO ('1'),
            // incluso cuando la factura se crea en empresa espejo ('3') por traspaso.
            Cliente clienteEntrega = servicio.CargarCliente(Constantes.Empresas.EMPRESA_POR_DEFECTO, cabFactura.Nº_Cliente, cabFactura.Contacto);
            Cliente clienteRazonSocial = clienteEntrega;
            if (!clienteRazonSocial.ClientePrincipal)
            {
                clienteRazonSocial = servicio.CargarClientePrincipal(Constantes.Empresas.EMPRESA_POR_DEFECTO, cabFactura.Nº_Cliente);
            }

            Empresa empresaFactura = servicio.CargarEmpresa(empresa);
            DireccionFactura direccionEmpresa = CargarDireccionEmpresa(empresaFactura);
            DireccionFactura direccionRazonSocial = CargarDireccionRazonSocial(clienteRazonSocial);
            DireccionFactura direccionEntrega = CargarDireccionEntrega(clienteEntrega);

            List<DireccionFactura> direcciones = new List<DireccionFactura>
            {
                direccionEmpresa,
                direccionRazonSocial,
                direccionEntrega
            };

            List<LineaFactura> lineas = new List<LineaFactura>();
            foreach (LinPedidoVta linea in cabFactura.LinPedidoVtas?.OrderBy(l => l.Nº_Orden))
            {
                LineaFactura lineaNueva = new LineaFactura
                {
                    Albaran = (int)linea.Nº_Albarán,
                    FechaAlbaran = (DateTime)linea.Fecha_Albarán,
                    Cantidad = linea.Cantidad,
                    Descripcion = linea.Texto?.Trim(),
                    Descuento = linea.SumaDescuentos,
                    Importe = linea.Base_Imponible,
                    PrecioUnitario = linea.Precio,
                    Producto = linea.Producto?.Trim(),
                    Pedido = linea.Número,
                    Estado = linea.Estado,
                    Picking = linea.Picking ?? 0
                };

                if (linea.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                {
                    Producto producto = servicio.CargarProducto(linea.Empresa, linea.Producto);
                    lineaNueva.Tamanno = producto.Tamaño;
                    lineaNueva.UnidadMedida = producto.UnidadMedida?.Trim();
                }
                lineas.Add(lineaNueva);
            }

            decimal importeTotal = 0;

            List<TotalFactura> totales = new List<TotalFactura>();
            var gruposTotal = cabFactura.LinPedidoVtas.GroupBy(l => new { l.PorcentajeIVA, l.PorcentajeRE });
            foreach (var grupoTotal in gruposTotal)
            {
                IEnumerable<LinPedidoVta> lineasGrupo = cabFactura.LinPedidoVtas.Where(l => l.PorcentajeIVA == grupoTotal.Key.PorcentajeIVA && l.PorcentajeRE == grupoTotal.Key.PorcentajeRE);
                TotalFactura total = new TotalFactura
                {
                    BaseImponible = lineasGrupo.Sum(l => l.Base_Imponible),
                    ImporteIVA = Math.Round(lineasGrupo.Sum(l => l.ImporteIVA), 2, MidpointRounding.AwayFromZero),
                    ImporteRecargoEquivalencia = Math.Round(lineasGrupo.Sum(l => l.PorcentajeRE * l.Base_Imponible), 2, MidpointRounding.AwayFromZero),
                    PorcentajeIVA = grupoTotal.Key.PorcentajeIVA / 100M,
                    PorcentajeRecargoEquivalencia = grupoTotal.Key.PorcentajeRE
                };
                totales.Add(total);
                importeTotal += total.BaseImponible + total.ImporteIVA + total.ImporteRecargoEquivalencia;
            }
            importeTotal = Math.Round(importeTotal, 2, MidpointRounding.AwayFromZero);


            List<VendedorFactura> vendedores = servicio.CargarVendedoresFactura(empresa, numeroFactura);
            List<VencimientoFactura> vencimientos = servicio.CargarVencimientosExtracto(empresa, clienteRazonSocial.Nº_Cliente, numeroFactura);
            if (vencimientos.Count > 1)
            {
                List<VencimientoFactura> vencimientosPendientes = vencimientos.Where(v => v.ImportePendiente != 0).ToList();
                if (vencimientosPendientes.Sum(v => v.ImportePendiente) == importeTotal)
                {
                    vencimientos = vencimientosPendientes;
                }
                else
                {
                    decimal totalAcumulado = 0;
                    int i = 0;

                    while (i < vencimientos.Count)
                    {
                        totalAcumulado += vencimientos[i].Importe;
                        if (totalAcumulado == 0)
                        {
                            vencimientos.RemoveRange(0, i + 1);
                            i = 0;
                        }
                        else
                        {
                            if (i > 0 && vencimientos[i].Importe < 0)
                            {
                                vencimientos[i - 1].Importe += vencimientos[i].Importe;
                                vencimientos.RemoveAt(i);
                            }
                            i++;
                        }
                    }
                }
            }

            if (vencimientos.Sum(v => v.Importe) != importeTotal)
            {
                vencimientos = servicio.CargarVencimientosOriginales(empresa, clienteRazonSocial.Nº_Cliente, numeroFactura);
            }

            if (vencimientos.Sum(v => v.Importe) != importeTotal)
            {
                // Diagnóstico detallado para identificar la causa del descuadre
                var sumaVencimientos = vencimientos.Sum(v => v.Importe);
                var diferencia = importeTotal - sumaVencimientos;
                var detalleVencimientos = vencimientos.Count > 0
                    ? string.Join(", ", vencimientos.Select(v => $"{v.Importe:F2}€"))
                    : "SIN VENCIMIENTOS";
                throw new Exception(
                    $"No cuadran los vencimientos con el total de la factura. " +
                    $"Total calculado: {importeTotal:F2}€, Suma vencimientos: {sumaVencimientos:F2}€, " +
                    $"Diferencia: {diferencia:F2}€, Vencimientos encontrados: {vencimientos.Count} [{detalleVencimientos}], " +
                    $"Empresa búsqueda: {empresa}, Cliente: {clienteRazonSocial.Nº_Cliente?.Trim()}");
            }

            foreach (VencimientoFactura vencimiento in vencimientos)
            {
                if (vencimiento.CCC != null && vencimiento.CCC.Trim() != "")
                {
                    continue;
                }

                vencimiento.Iban = vencimiento.FormaPago == "TRN" || vencimiento.FormaPago == "CNF" ? servicio.CuentaBancoEmpresa(empresa) : "<<< No Procede >>>";
            }

            string tipoDocumento = string.Empty;

            tipoDocumento = totales.Sum(t => t.BaseImponible) >= 0
                ? Constantes.Facturas.TiposDocumento.FACTURA
                : Constantes.Facturas.TiposDocumento.FACTURA_RECTIFICATIVA;


            Factura factura = new Factura
            {
                Cliente = cabFactura.Nº_Cliente.Trim(),
                Comentarios = cabPedido?.Comentarios?.Trim(),
                CorreoDesde = serieFactura.CorreoDesdeFactura,
                Delegacion = primeraLinea.Delegación?.Trim(),
                Direcciones = direcciones,
                DatosRegistrales = empresaFactura.TextoFactura?.Trim(),
                Fecha = cabFactura.Fecha,
                ImporteTotal = importeTotal,
                Lineas = lineas,
                Nif = clienteRazonSocial.CIF_NIF?.Trim(),
                NotasAlPie = serieFactura.Notas,
                NumeroFactura = cabFactura.Número?.Trim(),
                Ruta = cabPedido.Ruta?.Trim(),
                RutaInforme = serieFactura.RutaInforme,
                Serie = cabFactura.Serie?.Trim(),
                TipoDocumento = tipoDocumento,
                Totales = totales,
                Vencimientos = vencimientos.OrderBy(v => v.Vencimiento).ToList(),
                Vendedores = vendedores
            };

            return factura;
        }

        public Factura LeerPedido(string empresa, int pedido)
        {
            CabPedidoVta cabPedido = servicio.CargarCabPedido(empresa, pedido);
            LinPedidoVta primeraLinea = cabPedido.LinPedidoVtas.FirstOrDefault();
            ISerieFactura serieFactura = LeerSerie(cabPedido.Serie);

            Cliente clienteEntrega = servicio.CargarCliente(cabPedido.Empresa, cabPedido.Nº_Cliente, cabPedido.Contacto);
            Cliente clienteRazonSocial = clienteEntrega;
            if (!clienteRazonSocial.ClientePrincipal)
            {
                clienteRazonSocial = servicio.CargarClientePrincipal(cabPedido.Empresa, cabPedido.Nº_Cliente);
            }

            Empresa empresaPedido = servicio.CargarEmpresa(empresa);
            DireccionFactura direccionEmpresa = CargarDireccionEmpresa(empresaPedido);
            DireccionFactura direccionRazonSocial = CargarDireccionRazonSocial(clienteRazonSocial);
            DireccionFactura direccionEntrega = CargarDireccionEntrega(clienteEntrega);

            List<DireccionFactura> direcciones = new List<DireccionFactura>
            {
                direccionEmpresa,
                direccionRazonSocial,
                direccionEntrega
            };


            string tipoDocumento = cabPedido.LinPedidoVtas.Any(l => l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO)
                ? Constantes.Facturas.TiposDocumento.FACTURA_PROFORMA
                : cabPedido.NotaEntrega ? Constantes.Facturas.TiposDocumento.NOTA_ENTREGA : Constantes.Facturas.TiposDocumento.PEDIDO;
            bool ponerPrecios = tipoDocumento == Constantes.Facturas.TiposDocumento.FACTURA_PROFORMA || (!cabPedido.NotaEntrega && clienteEntrega.AlbaranValorado);

            List<LineaFactura> lineas = new List<LineaFactura>();
            foreach (LinPedidoVta linea in cabPedido.LinPedidoVtas?.OrderBy(l => l.Nº_Orden))
            {
                LineaFactura lineaNueva = new LineaFactura
                {
                    Albaran = linea.Nº_Albarán != null ? (int)linea.Nº_Albarán : 0,
                    FechaAlbaran = linea.Fecha_Albarán != null ? (DateTime)linea.Fecha_Albarán : DateTime.MinValue,
                    Cantidad = linea.Cantidad,
                    Descripcion = linea.Texto?.Trim(),
                    Descuento = ponerPrecios ? linea.SumaDescuentos : 0,
                    Importe = ponerPrecios ? linea.Base_Imponible : 0,
                    PrecioUnitario = ponerPrecios ? linea.Precio : 0,
                    Producto = linea.Producto?.Trim(),
                    Pedido = linea.Número,
                    Estado = linea.Estado,
                    Picking = linea.Picking ?? 0
                };

                if (linea.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                {
                    Producto producto = servicio.CargarProducto(linea.Empresa, linea.Producto);
                    lineaNueva.Tamanno = producto.Tamaño;
                    lineaNueva.UnidadMedida = producto.UnidadMedida?.Trim();
                }
                lineas.Add(lineaNueva);
            }

            decimal importeTotal = 0;

            List<TotalFactura> totales = new List<TotalFactura>();
            if (ponerPrecios)
            {
                var gruposTotal = cabPedido.LinPedidoVtas.GroupBy(l => new { l.PorcentajeIVA, l.PorcentajeRE });
                foreach (var grupoTotal in gruposTotal)
                {
                    IEnumerable<LinPedidoVta> lineasGrupo = cabPedido.LinPedidoVtas.Where(l => l.PorcentajeIVA == grupoTotal.Key.PorcentajeIVA && l.PorcentajeRE == grupoTotal.Key.PorcentajeRE);
                    TotalFactura total = new TotalFactura
                    {
                        BaseImponible = lineasGrupo.Sum(l => l.Base_Imponible),
                        ImporteIVA = Math.Round(lineasGrupo.Sum(l => l.ImporteIVA), 2, MidpointRounding.AwayFromZero),
                        ImporteRecargoEquivalencia = Math.Round(lineasGrupo.Sum(l => l.ImporteRE), 2, MidpointRounding.AwayFromZero),
                        PorcentajeIVA = grupoTotal.Key.PorcentajeIVA / 100M,
                        PorcentajeRecargoEquivalencia = grupoTotal.Key.PorcentajeRE
                    };
                    totales.Add(total);
                    importeTotal += total.BaseImponible + total.ImporteIVA + total.ImporteRecargoEquivalencia;
                }
            }

            List<VendedorFactura> vendedores = servicio.CargarVendedoresPedido(empresa, pedido);
            PlazoPago plazoPago = servicio.CargarPlazosPago(empresa, cabPedido.PlazosPago);
            List<EfectoPedidoVenta> efectos = servicio.CargarEfectosPedido(empresa, pedido);
            List<VencimientoFactura> vencimientos = efectos != null && efectos.Any() ?
                CalcularVencimientos(efectos) :
                CalcularVencimientos(importeTotal, plazoPago, cabPedido.Forma_Pago,
                    cabPedido.CCC, (DateTime)cabPedido.Primer_Vencimiento);
            if (vencimientos.Sum(v => v.Importe) != importeTotal)
            {
                throw new Exception("No cuadran los vencimientos con el total de la factura");
            }

            foreach (VencimientoFactura vencimiento in vencimientos)
            {
                // Issue #66: Ocultar estado de pago en pedidos/proformas porque no tienen pagos reales
                vencimiento.OcultarEstadoPago = true;

                if (!string.IsNullOrWhiteSpace(vencimiento.CCC))
                {
                    Iban iban = new Iban(servicio.ComponerIban(cabPedido.Empresa, cabPedido.Nº_Cliente, cabPedido.Contacto, cabPedido.CCC));
                    vencimiento.Iban = iban.Enmascarado;
                    continue;
                }

                vencimiento.Iban = vencimiento.FormaPago == "TRN" || vencimiento.FormaPago == "CNF" ? servicio.CuentaBancoEmpresa(empresa) : "<<< No Procede >>>";
            }
            Factura factura = new Factura
            {
                Cliente = cabPedido.Nº_Cliente.Trim(),
                Comentarios = cabPedido?.Comentarios?.Trim(),
                CorreoDesde = serieFactura.CorreoDesdeFactura,
                Delegacion = primeraLinea.Delegación?.Trim(),
                Direcciones = direcciones,
                DatosRegistrales = empresaPedido.TextoFactura?.Trim(),
                Fecha = (DateTime)cabPedido.Fecha,
                ImporteTotal = importeTotal,
                Lineas = lineas,
                Nif = clienteRazonSocial.CIF_NIF?.Trim(),
                NotasAlPie = serieFactura.Notas,
                NumeroFactura = cabPedido.Número.ToString(),
                Ruta = cabPedido.Ruta?.Trim(),
                RutaInforme = serieFactura.RutaInforme,
                Serie = cabPedido.Serie?.Trim(),
                TipoDocumento = tipoDocumento,
                Totales = totales,
                Vencimientos = ponerPrecios ? vencimientos.OrderBy(v => v.Vencimiento).ToList() : new List<VencimientoFactura>(),
                Vendedores = vendedores
            };

            if (!ponerPrecios && !cabPedido.NotaEntrega)
            {
                NotaFactura nota = new NotaFactura
                {
                    Nota = "Este documento se muestra sin valoración económica. Si desea recibir documentos valorados, no dude en comunicárnoslo. Gracias."
                };
                factura.NotasAlPie.Add(nota);
            }

            return factura;
        }

        public static List<VencimientoFactura> CalcularVencimientos(List<EfectoPedidoVenta> efectos)
        {
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>();
            foreach (EfectoPedidoVenta efecto in efectos)
            {
                VencimientoFactura vencimiento = new VencimientoFactura
                {
                    CCC = efecto.CCC,
                    Importe = efecto.Importe,
                    ImportePendiente = efecto.Importe,
                    FormaPago = efecto.FormaPago,
                    Vencimiento = efecto.FechaVencimiento
                };
                vencimientos.Add(vencimiento);
            }
            return vencimientos;
        }

        public static List<VencimientoFactura> CalcularVencimientos(decimal importe, PlazoPago plazoPago, string formaPago, string ccc, DateTime primerVencimiento)
        {
            if (plazoPago == null || plazoPago.Nº_Plazos < 1)
            {
                throw new ArgumentException("No es posible hacer menos de un plazo");
            }
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>();
            decimal importePorAplicar = importe;
            for (int i = 0; i < plazoPago.Nº_Plazos; i++)
            {
                decimal importeVencimiento = i == plazoPago.Nº_Plazos - 1 ? importePorAplicar : Math.Round(importe / plazoPago.Nº_Plazos, 2, MidpointRounding.AwayFromZero);
                VencimientoFactura vencimiento = new VencimientoFactura
                {
                    CCC = ccc,
                    Importe = importeVencimiento,
                    ImportePendiente = importeVencimiento,
                    FormaPago = formaPago,
                    Vencimiento = primerVencimiento.AddDays(i * plazoPago.DíasEntrePlazos).AddMonths(i * plazoPago.MesesEntrePlazos)
                };
                vencimientos.Add(vencimiento);
                importePorAplicar -= importeVencimiento;
            }

            return vencimientos;
        }

        private static DireccionFactura CargarDireccionEntrega(Cliente clienteEntrega)
        {
            return new DireccionFactura
            {
                Tipo = "Entrega",
                Nombre = clienteEntrega.Nombre?.Trim(),
                CodigoPostal = clienteEntrega.CodPostal?.Trim(),
                Direccion = clienteEntrega.Dirección?.Trim(),
                Poblacion = clienteEntrega.Población?.Trim(),
                Provincia = clienteEntrega.Provincia?.Trim(),
                Telefonos = clienteEntrega.Teléfono?.Trim()
            };
        }

        private static DireccionFactura CargarDireccionRazonSocial(Cliente clienteRazonSocial)
        {
            return new DireccionFactura
            {
                Tipo = "Fiscal",
                Nombre = clienteRazonSocial.Nombre?.Trim(),
                CodigoPostal = clienteRazonSocial.CodPostal?.Trim(),
                Direccion = clienteRazonSocial.Dirección?.Trim(),
                Poblacion = clienteRazonSocial.Población?.Trim(),
                Provincia = clienteRazonSocial.Provincia?.Trim(),
                Telefonos = clienteRazonSocial.Teléfono?.Trim()
            };
        }

        private static DireccionFactura CargarDireccionEmpresa(Empresa empresaPedido)
        {
            return new DireccionFactura
            {
                Tipo = "Empresa",
                Nombre = empresaPedido.Nombre?.Trim(),
                CodigoPostal = empresaPedido.CodPostal?.Trim(),
                Direccion = empresaPedido.Dirección?.Trim() + "\n" + empresaPedido.Dirección2?.Trim(),
                Comentarios = empresaPedido.Texto?.Trim(),
                Poblacion = empresaPedido.Población?.Trim(),
                Provincia = empresaPedido.Provincia?.Trim(),
                Telefonos = empresaPedido.Teléfono?.Trim()
            };
        }

        public static ISerieFactura LeerSerie(string serie)
        {
            string claseSerie = "NestoAPI.Models.Facturas.SeriesFactura.Serie" + serie.Trim();
            Type elementType = Type.GetType(claseSerie);
            ISerieFactura serieFactura = (ISerieFactura)Activator.CreateInstance(elementType);
            return serieFactura;
        }

        public List<Factura> LeerFacturas(List<FacturaLookup> numerosFactura)
        {
            List<Factura> facturas = new List<Factura>();

            foreach (FacturaLookup factura in numerosFactura)
            {
                try
                {
                    Factura nuevaFactura = int.TryParse(factura.Factura, out int numeroPedido)
                        ? LeerPedido(factura.Empresa, numeroPedido)
                        : LeerFactura(factura.Empresa, factura.Factura);
                    facturas.Add(nuevaFactura);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error al leer la factura/pedido {factura.Factura} de la empresa {factura.Empresa}: {ex.Message}", ex);
                }

            }

            return facturas;
        }

        public List<Factura> LeerAlbaranes(List<FacturaLookup> numerosAlbaran)
        {
            List<Factura> albaranes = new List<Factura>();

            foreach (FacturaLookup albaran in numerosAlbaran)
            {
                if (int.TryParse(albaran.Factura, out int numeroAlbaran))
                {
                    Factura nuevoAlbaran = LeerAlbaran(albaran.Empresa, numeroAlbaran);
                    albaranes.Add(nuevoAlbaran);
                }
            }

            return albaranes;
        }

        public Factura LeerAlbaran(string empresa, int numeroAlbaran)
        {
            // Buscar el pedido que contiene este albarán
            CabPedidoVta cabPedido = servicio.CargarCabPedidoPorAlbaran(empresa, numeroAlbaran);

            if (cabPedido == null)
            {
                throw new Exception($"No se encontró ningún pedido con el albarán {numeroAlbaran} en la empresa {empresa}");
            }

            LinPedidoVta primeraLinea = cabPedido.LinPedidoVtas.FirstOrDefault(l => l.Nº_Albarán == numeroAlbaran);

            if (primeraLinea == null)
            {
                throw new Exception($"No se encontró ninguna línea con el albarán {numeroAlbaran}");
            }

            ISerieFactura serieFactura = LeerSerie(cabPedido.Serie);

            Cliente clienteEntrega = servicio.CargarCliente(cabPedido.Empresa, cabPedido.Nº_Cliente, cabPedido.Contacto);
            Cliente clienteRazonSocial = clienteEntrega;
            if (!clienteRazonSocial.ClientePrincipal)
            {
                clienteRazonSocial = servicio.CargarClientePrincipal(cabPedido.Empresa, cabPedido.Nº_Cliente);
            }

            Empresa empresaAlbaran = servicio.CargarEmpresa(empresa);
            DireccionFactura direccionEmpresa = CargarDireccionEmpresa(empresaAlbaran);
            DireccionFactura direccionRazonSocial = CargarDireccionRazonSocial(clienteRazonSocial);
            DireccionFactura direccionEntrega = CargarDireccionEntrega(clienteEntrega);

            List<DireccionFactura> direcciones = new List<DireccionFactura>
            {
                direccionEmpresa,
                direccionRazonSocial,
                direccionEntrega
            };

            bool ponerPrecios = clienteEntrega.AlbaranValorado;

            // Filtrar solo las líneas del albarán especificado (estado >= 2)
            List<LineaFactura> lineas = new List<LineaFactura>();
            var lineasAlbaranQuery = cabPedido.LinPedidoVtas?.Where(l => l.Nº_Albarán == numeroAlbaran && l.Estado >= Constantes.EstadosLineaVenta.ALBARAN).OrderBy(l => l.Nº_Orden);

            if (lineasAlbaranQuery == null || !lineasAlbaranQuery.Any())
            {
                throw new Exception($"No se encontraron líneas con estado ALBARAN (>= 2) para el albarán {numeroAlbaran}. Pedido: {cabPedido.Número}");
            }

            foreach (LinPedidoVta linea in lineasAlbaranQuery)
            {
                LineaFactura lineaNueva = new LineaFactura
                {
                    Albaran = (int)linea.Nº_Albarán,
                    FechaAlbaran = (DateTime)linea.Fecha_Albarán,
                    Cantidad = linea.Cantidad,
                    Descripcion = linea.Texto?.Trim(),
                    Descuento = ponerPrecios ? linea.SumaDescuentos : 0,
                    Importe = ponerPrecios ? linea.Base_Imponible : 0,
                    PrecioUnitario = ponerPrecios ? linea.Precio : 0,
                    Producto = linea.Producto?.Trim(),
                    Pedido = linea.Número,
                    Estado = linea.Estado,
                    Picking = linea.Picking ?? 0
                };

                if (linea.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                {
                    Producto producto = servicio.CargarProducto(linea.Empresa, linea.Producto);
                    lineaNueva.Tamanno = producto.Tamaño;
                    lineaNueva.UnidadMedida = producto.UnidadMedida?.Trim();
                }
                lineas.Add(lineaNueva);
            }

            decimal importeTotal = 0;

            List<TotalFactura> totales = new List<TotalFactura>();
            if (ponerPrecios)
            {
                var lineasAlbaran = cabPedido.LinPedidoVtas.Where(l => l.Nº_Albarán == numeroAlbaran && l.Estado >= Constantes.EstadosLineaVenta.ALBARAN);
                var gruposTotal = lineasAlbaran.GroupBy(l => new { l.PorcentajeIVA, l.PorcentajeRE });
                foreach (var grupoTotal in gruposTotal)
                {
                    IEnumerable<LinPedidoVta> lineasGrupo = lineasAlbaran.Where(l => l.PorcentajeIVA == grupoTotal.Key.PorcentajeIVA && l.PorcentajeRE == grupoTotal.Key.PorcentajeRE);
                    TotalFactura total = new TotalFactura
                    {
                        BaseImponible = lineasGrupo.Sum(l => l.Base_Imponible),
                        ImporteIVA = Math.Round(lineasGrupo.Sum(l => l.ImporteIVA), 2, MidpointRounding.AwayFromZero),
                        ImporteRecargoEquivalencia = Math.Round(lineasGrupo.Sum(l => l.ImporteRE), 2, MidpointRounding.AwayFromZero),
                        PorcentajeIVA = grupoTotal.Key.PorcentajeIVA / 100M,
                        PorcentajeRecargoEquivalencia = grupoTotal.Key.PorcentajeRE
                    };
                    totales.Add(total);
                    importeTotal += total.BaseImponible + total.ImporteIVA + total.ImporteRecargoEquivalencia;
                }
            }

            List<VendedorFactura> vendedores = servicio.CargarVendedoresPedido(empresa, cabPedido.Número);

            // Los albaranes no tienen vencimientos
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>();

            Factura albaran = new Factura
            {
                Cliente = cabPedido.Nº_Cliente.Trim(),
                Comentarios = cabPedido?.Comentarios?.Trim(),
                CorreoDesde = serieFactura.CorreoDesdeFactura,
                Delegacion = primeraLinea?.Delegación?.Trim(),
                Direcciones = direcciones,
                DatosRegistrales = empresaAlbaran.TextoFactura?.Trim(),
                Fecha = primeraLinea?.Fecha_Albarán ?? DateTime.Today,
                ImporteTotal = importeTotal,
                Lineas = lineas,
                Nif = clienteRazonSocial.CIF_NIF?.Trim(),
                NotasAlPie = serieFactura.Notas,
                NumeroFactura = numeroAlbaran.ToString(),
                Ruta = cabPedido.Ruta?.Trim(),
                RutaInforme = serieFactura.RutaInforme,
                Serie = cabPedido.Serie?.Trim(),
                TipoDocumento = Constantes.Facturas.TiposDocumento.ALBARAN,
                Totales = totales,
                Vendedores = vendedores,
                Vencimientos = vencimientos
            };

            return albaran;
        }
        public async Task<IEnumerable<FacturaCorreo>> EnviarFacturasPorCorreo(DateTime dia)
        {
            IEnumerable<FacturaCorreo> facturasCorreo = servicio.LeerFacturasDia(dia);

            // CONTROL DE SEGURIDAD TEMPORAL (QUITAR DESPUÉS DE UNOS DÍAS):
            // Capa extra de protección hard-coded para serie GB
            var facturasGB = facturasCorreo.Where(f => f.Factura.Length >= 2 && f.Factura.Substring(0, 2).ToUpper() == "GB").ToList();
            if (facturasGB.Any())
            {
                string listaFacturasGB = string.Join(", ", facturasGB.Select(f => f.Factura));
                string mensajeError = $"❌ SEGURIDAD: Se detectaron {facturasGB.Count} factura(s) de serie GB en el envío diario: {listaFacturasGB}. Envío de correo CANCELADO.";
                System.Diagnostics.Debug.WriteLine(mensajeError);
                throw new InvalidOperationException(mensajeError);
            }

            List<MailMessage> listaCorreos = new List<MailMessage>();
            string mailAnterior = string.Empty;
            MailMessage mail = new MailMessage();
            foreach (FacturaCorreo fra in facturasCorreo)
            {
                if (fra.Correo != mailAnterior)
                {
                    if (!string.IsNullOrEmpty(mailAnterior))
                    {
                        mail.Subject = mail.Subject.Trim().TrimEnd(',');
                        listaCorreos.Add(mail);
                    }
                    mailAnterior = fra.Correo;
                    ISerieFactura serieFactura;
                    try
                    {
                        serieFactura = LeerSerie(fra.Factura.Substring(0, 2));
                    }
                    catch
                    {
                        mail = new MailMessage();
                        mailAnterior = string.Empty;
                        continue;
                    }

                    // Si la serie no permite envío por email (CorreoDesdeFactura == null), omitir esta factura
                    if (serieFactura.CorreoDesdeFactura == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠ Serie {fra.Factura.Substring(0, 2)} no permite envío por correo - factura {fra.Factura} omitida");
                        mail = new MailMessage();
                        mailAnterior = string.Empty;
                        continue;
                    }

                    try
                    {
                        mail = new MailMessage(serieFactura.CorreoDesdeFactura.Address, fra.Correo)
                        {
                            Subject = "Facturación nº "
                        };
                    }
                    catch
                    {
                        mail.From = new MailAddress(serieFactura.CorreoDesdeFactura.Address);
                        mail.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
                        mail.Subject = string.Format("[ERROR: {0}] Facturación nº ", fra.Correo);
                    }

                    mail.Bcc.Add(new MailAddress("carlosadrian@nuevavision.es"));
                    mail.Bcc.Add(new MailAddress("lauramagan@nuevavision.es"));

                    mail.Body = (await GenerarCorreoHTML(fra)).ToString();
                    mail.IsBodyHtml = true;
                }
                mail.Subject += fra.Factura + ", ";
                ByteArrayContent facturaPdf = FacturaEnPDF(fra.Empresa, fra.Factura);
                Attachment attachment = new Attachment(new MemoryStream(await facturaPdf.ReadAsByteArrayAsync()), fra.Factura + ".pdf");
                mail.Attachments.Add(attachment);
            }
            mail.Subject = mail.Subject.Trim().TrimEnd(',');
            listaCorreos.Add(mail);
            SmtpClient client = CrearClienteSMTP();

            foreach (MailMessage correo in listaCorreos)
            {
                //A veces no conecta a la primera, por lo que reintentamos 2s después
                try
                {
                    client.Send(correo);
                }
                catch
                {
                    await Task.Delay(2000);
                    try
                    {
                        client.Send(correo);
                    }
                    catch
                    {
                        correo.To.Clear();
                        correo.To.Add(Constantes.Correos.CORREO_ADMON);
                        correo.Subject = "[ERROR] " + correo.Subject;
                        await Task.Delay(2000);
                        client.Send(correo);
                    }
                }
            }

            return facturasCorreo;
        }

        private static SmtpClient CrearClienteSMTP()
        {
            SmtpClient client = new SmtpClient
            {
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };
            string contrasenna = ConfigurationManager.AppSettings["office365password"];
            client.Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", contrasenna);
            client.Host = "smtp.office365.com";
            client.TargetName = "STARTTLS/smtp.office365.com"; // Añadir esta línea para especificar el nombre del objetivo para STARTTLS

            // Configurar TLS 1.2 explícitamente
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            return client;
        }

        public List<ClienteCorreoFactura> EnviarFacturasTrimestrePorCorreo(DateTime firstDayOfQuarter, DateTime lastDayOfQuarter)
        {
            List<ClienteCorreoFactura> clientesCorreo = servicio.LeerClientesCorreo(firstDayOfQuarter, lastDayOfQuarter);

            //bool antesDelError = true;
            int contador = 0;

            foreach (ClienteCorreoFactura cliente in clientesCorreo)
            {

                contador++;
                //if (cliente.Cliente.Trim() == "17765" && cliente.Contacto.Trim() == "0")
                //{
                //    antesDelError = false;
                //    continue;
                //}
                //if (antesDelError)
                //{
                //    continue;
                //}

                IEnumerable<FacturaCorreo> facturasCorreo = servicio.LeerFacturasCliente(cliente.Cliente, cliente.Contacto, firstDayOfQuarter, lastDayOfQuarter);

                // CONTROL DE SEGURIDAD TEMPORAL (QUITAR DESPUÉS DE UNOS DÍAS):
                // Capa extra de protección hard-coded para serie GB
                var facturasGB = facturasCorreo.Where(f => f.Factura.Length >= 2 && f.Factura.Substring(0, 2).ToUpper() == "GB").ToList();
                if (facturasGB.Any())
                {
                    string listaFacturasGB = string.Join(", ", facturasGB.Select(f => f.Factura));
                    string mensajeError = $"❌ SEGURIDAD: Cliente {cliente.Cliente}/{cliente.Contacto} tiene {facturasGB.Count} factura(s) de serie GB: {listaFacturasGB}. Envío OMITIDO para este cliente.";
                    System.Diagnostics.Debug.WriteLine(mensajeError);
                    continue; // Saltar este cliente completo
                }

                // Verificar si alguna factura es de una serie que permite envío por email
                bool todasSeriesBloqueadas = true;
                foreach (FacturaCorreo fra in facturasCorreo)
                {
                    try
                    {
                        ISerieFactura serieTest = LeerSerie(fra.Factura.Substring(0, 2));
                        if (serieTest.CorreoDesdeFactura != null)
                        {
                            todasSeriesBloqueadas = false;
                            break;
                        }
                    }
                    catch
                    {
                        // Ignorar series con errores
                    }
                }

                // Si todas las facturas son de series bloqueadas, saltar este cliente
                if (todasSeriesBloqueadas)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠ Cliente {cliente.Cliente}/{cliente.Contacto} tiene solo facturas de series bloqueadas - omitido del envío trimestral");
                    continue;
                }

                List<Attachment> facturasAdjuntas = new List<Attachment>();
                foreach (FacturaCorreo fra in facturasCorreo)
                {
                    try
                    {
                        // Verificar si esta factura específica es de una serie bloqueada
                        ISerieFactura serieFactura = LeerSerie(fra.Factura.Substring(0, 2));
                        if (serieFactura.CorreoDesdeFactura == null)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠ Factura {fra.Factura} de serie bloqueada - omitida del envío trimestral");
                            continue;
                        }

                        using (ByteArrayContent facturaPdf = FacturaEnPDF(fra.Empresa, fra.Factura))
                        {
                            byte[] facturaBytes = facturaPdf.ReadAsByteArrayAsync().Result;
                            Attachment attachment = new Attachment(new MemoryStream(facturaBytes), fra.Factura + ".pdf");
                            facturasAdjuntas.Add(attachment);
                        }
                    }
                    catch
                    {
                        continue;
                    }

                }

                using (MailMessage mail = new MailMessage())
                {
                    try
                    {
                        mail.To.Add(cliente.Correo);
                        mail.Subject = string.Format("Facturas del trimestre del {0} al {1} (cliente {2}/{3})",
                            firstDayOfQuarter.ToString("d"), lastDayOfQuarter.ToString("d"), cliente.Cliente.Trim(), cliente.Contacto.Trim());
                        mail.Bcc.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));

                        // Buscar la primera factura de una serie que permita email para obtener el remitente
                        ISerieFactura serieFactura = null;
                        foreach (var fra in facturasCorreo)
                        {
                            try
                            {
                                var serie = LeerSerie(fra.Factura.Substring(0, 2));
                                if (serie.CorreoDesdeFactura != null)
                                {
                                    serieFactura = serie;
                                    break;
                                }
                            }
                            catch { }
                        }

                        if (serieFactura == null || serieFactura.CorreoDesdeFactura == null)
                        {
                            throw new InvalidOperationException("No se encontró ninguna serie válida para envío por correo");
                        }

                        mail.From = new MailAddress(serieFactura.CorreoDesdeFactura.Address);
                        mail.Body = GenerarCorreoTrimestreHTML(serieFactura);
                        mail.IsBodyHtml = true;
                        foreach (Attachment adjunta in facturasAdjuntas)
                        {
                            mail.Attachments.Add(adjunta);
                        }
                    }
                    catch
                    {
                        mail.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
                        mail.Subject = string.Format("[ERROR: {0}] Facturas del trimestre", cliente.Correo);
                    }

                    bool exito = servicio.EnviarCorreoSMTP(mail);
                    if (!exito)
                    {
                        mail.Subject = "[ERROR] " + mail.Subject;
                        mail.To.Clear();
                        mail.To.Add(Constantes.Correos.CORREO_ADMON);
                        System.Threading.Thread.Sleep(1000);
                        _ = servicio.EnviarCorreoSMTP(mail);
                    }
                    mail.Dispose();
                }
            }

            return clientesCorreo;
        }

        private async Task<StringBuilder> GenerarCorreoHTML(FacturaCorreo fra)
        {
            ISerieFactura serieFactura = LeerSerie(fra.Factura.Substring(0, 2));
            StringBuilder s = new StringBuilder();

            _ = s.AppendLine("<p>Adjunto le enviamos su facturación del día.</p>");
            _ = s.AppendLine("<br/>");
            _ = s.AppendLine("<p>La factura se ha generado hoy mismo, por lo que <strong>es lógico que aún no haya recibido los productos</strong>, pero se la adelantamos para que pueda llevar los controles pertinentes.</p>");
            _ = s.AppendLine("<br/>");
            _ = s.AppendLine("<p>¿Qué es la factura electrónica?</p>");
            _ = s.AppendLine("<ul><li>Una factura electrónica es, ante todo, una factura. Es decir, tiene los mismos efectos legales que una factura en papel.</li>");
            _ = s.AppendLine("<li>Recordemos que una factura es un justificante de la entrega de bienes o la prestación de servicios.</li>");
            _ = s.AppendLine("<li>Una factura electrónica es una factura que se expide y recibe en formato electrónico.</li>");
            _ = s.AppendLine("<li>La factura electrónica, por tanto, es una alternativa legal a la factura tradicional en papel.</li></ul>");
            _ = s.AppendLine("<br/>");
            _ = s.AppendLine("<p style=\"color: green;\">Nuestro compromiso con la protección del medio ambiente es firme, por lo que agradecemos que nos ayude a conseguirlo con la eliminación de las facturas en papel.</p>");
            _ = s.AppendLine("<br/>");
            // Sección para la descarga de la app
            _ = s.AppendLine("<p><strong>Ahora también puede descargar sus facturas desde nuestra aplicación en su móvil.</strong></p>");
            _ = s.AppendLine("<p>Descárguela ahora desde Google Play:</p>");
            _ = s.AppendLine("<a href=\"https://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas\" target=\"_blank\">");
            _ = s.AppendLine("<img src=\"https://upload.wikimedia.org/wikipedia/commons/7/78/Google_Play_Store_badge_EN.svg\" alt=\"Disponible en Google Play\" style=\"width: 150px; height: auto;\" />");
            _ = s.AppendLine("</a>");
            _ = s.AppendLine("<br/>");
            _ = s.AppendLine(serieFactura.FirmaCorreo);

            return s;
        }

        private string GenerarCorreoTrimestreHTML(ISerieFactura serieFactura)
        {
            StringBuilder s = new StringBuilder();

            _ = s.AppendLine("<p>Estimado cliente:</p>");
            _ = s.AppendLine("<p>Adjunto le enviamos todas sus facturas del trimestre para que se las pueda hacer llegar a su gestoría y facilitarle al máximo el trámite trimestral de impuestos.</p>");
            _ = s.AppendLine("<br/>");
            _ = s.AppendLine("<p>¿Qué es la factura electrónica?</p>");
            _ = s.AppendLine("<ul><li>Una factura electrónica es, ante todo, una factura. Es decir, tiene los mismos efectos legales que una factura en papel.</li>");
            _ = s.AppendLine("<li>Recordemos que una factura es un justificante de la entrega de bienes o la prestación de servicios.</li>");
            _ = s.AppendLine("<li>Una factura electrónica es una factura que se expide y recibe en formato electrónico.</li>");
            _ = s.AppendLine("<li>La factura electrónica, por tanto, es una alternativa legal a la factura tradicional en papel.</li></ul>");
            _ = s.AppendLine("<br/>");
            _ = s.AppendLine("<p style=\"color: green;\">Nuestro compromiso con la protección del medio ambiente es firme, por lo que agradecemos que nos ayude a conseguirlo con la eliminación de las facturas en papel.</p>");
            _ = s.AppendLine("<br/>");
            // Sección para la descarga de la app
            _ = s.AppendLine("<p><strong>Ahora también puede descargar sus facturas desde nuestra aplicación en su móvil.</strong></p>");
            _ = s.AppendLine("<p>Descárguela ahora desde Google Play:</p>");
            _ = s.AppendLine("<a href=\"https://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas\" target=\"_blank\">");
            _ = s.AppendLine("<img src=\"https://upload.wikimedia.org/wikipedia/commons/7/78/Google_Play_Store_badge_EN.svg\" alt=\"Disponible en Google Play\" style=\"width: 150px; height: auto;\" />");
            _ = s.AppendLine("</a>");
            _ = s.AppendLine("<br/>");
            _ = s.AppendLine(serieFactura.FirmaCorreo);

            return s.ToString();
        }

        public List<DireccionFactura> DireccionesFactura(Factura factura)
        {
            return factura.Direcciones;
        }
        public List<LineaFactura> LineasFactura(Factura factura)
        {
            //esto no vale para mucho, pero es necesario para meter el dataset en el informe
            return factura.Lineas;
        }
        public List<NotaFactura> NotasFactura(Factura factura)
        {
            return factura.NotasAlPie;
        }
        public List<TotalFactura> TotalesFactura(Factura factura)
        {
            return factura.Totales;
        }
        public List<VencimientoFactura> VencimientosFactura(Factura factura)
        {
            return factura.Vencimientos;
        }
        public List<VendedorFactura> VendedoresFactura(Factura factura)
        {
            return factura.Vendedores;
        }

        private static async Task<string> LeerProductosRecomendados()
        {
            // ESTÁ SIN USAR. 
            // TODO: PONER LOS PRODUCTOS RECOMENDADOS EN EL HTML DEL CORREO DE LA FACTURA
            // OJO, SOLO PARA FACTURA DE NV
            using (HttpClient client = new HttpClient())
            {
                var scoreRequest = new
                {

                    Inputs = new Dictionary<string, StringTable>() {
                        {
                            "input1",
                            new StringTable()
                            {
                                ColumnNames = new string[] {"userId", "productoId", "rating"},
                                Values = new string[,] {  { "15191/0", "0", "0" }  }
                            }
                        },
                    },
                    GlobalParameters = new Dictionary<string, string>()
                    {
                    }
                };
                string apiKey = ConfigurationManager.AppSettings["RecomendacionProductosApiKey"]; // Replace this with the API key for the web service
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                client.BaseAddress = new Uri("https://ussouthcentral.services.azureml.net/workspaces/d77f0a96e7af4f318b1d1b2a3d260851/services/77d259cbcaef42a9aeb146d12e183350/execute?api-version=2.0&details=true");

                // WARNING: The 'await' statement below can result in a deadlock if you are calling this code from the UI thread of an ASP.Net application.
                // One way to address this would be to call ConfigureAwait(false) so that the execution does not attempt to resume on the original context.
                // For instance, replace code such as:
                //      result = await DoSomeTask()
                // with the following:
                //      result = await DoSomeTask().ConfigureAwait(false)


                HttpResponseMessage response = await client.PostAsJsonAsync("", scoreRequest);

                if (response.IsSuccessStatusCode)
                {
                    string result = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("Result: {0}", result);
                    return result;
                }
                else
                {
                    Console.WriteLine(string.Format("The request failed with status code: {0}", response.StatusCode));

                    // Print the headers - they include the requert ID and the timestamp, which are useful for debugging the failure
                    Console.WriteLine(response.Headers.ToString());

                    string responseContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine(responseContent);
                    return responseContent;
                }
            }
        }

        public async Task<CrearFacturaResponseDTO> CrearFactura(string empresa, int pedido, string usuario)
        {
            // Delegar directamente al servicio
            // Las excepciones de negocio (FacturacionException) se propagan automáticamente
            return await servicio.CrearFactura(empresa, pedido, usuario);
        }
    }
}



public class StringTable
{
    public string[] ColumnNames { get; set; }
    public string[,] Values { get; set; }
}
