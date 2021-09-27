using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Reporting.WebForms;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Infraestructure.Facturas
{
    public class GestorFacturas : IGestorFacturas
    {
        IServicioFacturas servicio;
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
        public ByteArrayContent FacturasEnPDF(List<Factura> facturas)
        {
            Warning[] warnings;
            string mimeType;
            string[] streamids;
            string encoding;
            string filenameExtension;
            
            ReportViewer viewer = new ReportViewer();
            viewer.LocalReport.ReportPath = facturas.FirstOrDefault().RutaInforme;
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Facturas", facturas));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Direcciones", facturas.FirstOrDefault().Direcciones));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("LineasFactura", facturas.FirstOrDefault().Lineas));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Vendedores", facturas.FirstOrDefault().Vendedores));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Vencimientos", facturas.FirstOrDefault().Vencimientos));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("NotasAlPie", facturas.FirstOrDefault().NotasAlPie));
            viewer.LocalReport.DataSources.Add(new ReportDataSource("Totales", facturas.FirstOrDefault().Totales));

            viewer.LocalReport.Refresh();

            var bytes = viewer.LocalReport.Render("PDF", null, out mimeType, out encoding, out filenameExtension, out streamids, out warnings);

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

            CabPedidoVta cabPedido;
            if (primeraLinea != null)
            {
                cabPedido = servicio.CargarCabPedido(empresa, primeraLinea.Número);
            }
            else
            {
                cabPedido = null;
            }

            Cliente clienteEntrega = servicio.CargarCliente(cabFactura.Empresa, cabFactura.Nº_Cliente, cabFactura.Contacto);
            Cliente clienteRazonSocial = clienteEntrega;
            if (!clienteRazonSocial.ClientePrincipal)
            {
                clienteRazonSocial = servicio.CargarClientePrincipal(cabFactura.Empresa, cabFactura.Nº_Cliente);
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
                var lineasGrupo = cabFactura.LinPedidoVtas.Where(l => l.PorcentajeIVA == grupoTotal.Key.PorcentajeIVA && l.PorcentajeRE == grupoTotal.Key.PorcentajeRE);
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
                throw new Exception("No cuadran los vencimientos con el total de la factura");
            }

            foreach (var vencimiento in vencimientos)
            {
                if (vencimiento.CCC != null && vencimiento.CCC.Trim() != "")
                {
                    continue;
                }

                if (vencimiento.FormaPago == "TRN" || vencimiento.FormaPago == "CNF")
                {
                    vencimiento.Iban = servicio.CuentaBancoEmpresa(empresa);
                }
                else
                {
                    vencimiento.Iban = "<<< No Procede >>>";
                }
            }

            string tipoDocumento = string.Empty;

            if (totales.Sum(t => t.BaseImponible) >= 0)
            {
                tipoDocumento = Constantes.Facturas.TiposDocumento.FACTURA;
            } else
            {
                tipoDocumento = Constantes.Facturas.TiposDocumento.FACTURA_RECTIFICATIVA;
            }


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


            string tipoDocumento;
            if (cabPedido.LinPedidoVtas.Any(l => l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO))
            {
                tipoDocumento = Constantes.Facturas.TiposDocumento.FACTURA_PROFORMA;
            }
            else if (cabPedido.NotaEntrega)
            {
                tipoDocumento = Constantes.Facturas.TiposDocumento.NOTA_ENTREGA;
            }
            else
            {
                tipoDocumento = Constantes.Facturas.TiposDocumento.PEDIDO;
            }

            bool ponerPrecios = tipoDocumento == Constantes.Facturas.TiposDocumento.FACTURA_PROFORMA || (!cabPedido.NotaEntrega && clienteEntrega.AlbaranValorado);

            List<LineaFactura> lineas = new List<LineaFactura>();
            foreach (LinPedidoVta linea in cabPedido.LinPedidoVtas?.OrderBy(l => l.Nº_Orden))
            {
                LineaFactura lineaNueva = new LineaFactura
                {
                    Albaran =  linea.Nº_Albarán != null ? (int)linea.Nº_Albarán : 0,
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
                    var lineasGrupo = cabPedido.LinPedidoVtas.Where(l => l.PorcentajeIVA == grupoTotal.Key.PorcentajeIVA && l.PorcentajeRE == grupoTotal.Key.PorcentajeRE);
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
            
            List<VencimientoFactura> vencimientos = CalcularVencimientos(importeTotal, plazoPago, cabPedido.Forma_Pago, 
                cabPedido.CCC, (DateTime)cabPedido.Primer_Vencimiento);
            if (vencimientos.Sum(v => v.Importe) != importeTotal)
            {
                throw new Exception("No cuadran los vencimientos con el total de la factura");
            }

            foreach (var vencimiento in vencimientos)
            {
                if (!string.IsNullOrWhiteSpace(vencimiento.CCC))
                {
                    Iban iban = new Iban(servicio.ComponerIban(cabPedido.Empresa, cabPedido.Nº_Cliente, cabPedido.Contacto, cabPedido.CCC));
                    vencimiento.Iban = iban.Enmascarado;
                    continue;
                }

                if (vencimiento.FormaPago == "TRN" || vencimiento.FormaPago == "CNF")
                {
                    vencimiento.Iban = servicio.CuentaBancoEmpresa(empresa);
                }
                else
                {
                    vencimiento.Iban = "<<< No Procede >>>";
                }
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
                decimal importeVencimiento;
                if (i == plazoPago.Nº_Plazos -1 ) //último vencimiento
                {
                    importeVencimiento = importePorAplicar;
                } else
                {
                    importeVencimiento = Math.Round(importe / plazoPago.Nº_Plazos, 2, MidpointRounding.AwayFromZero);
                }
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

            foreach (var factura in numerosFactura)
            {
                try
                {
                    Factura nuevaFactura;
                    if (Int32.TryParse(factura.Factura, out int numeroPedido))
                    {
                        nuevaFactura = LeerPedido(factura.Empresa, numeroPedido);
                    }
                    else
                    {
                        nuevaFactura = LeerFactura(factura.Empresa, factura.Factura);
                    }
                    facturas.Add(nuevaFactura);
                } catch
                {
                    continue;
                }
                
            }

            return facturas;
        }                
        public async Task<IEnumerable<FacturaCorreo>> EnviarFacturasPorCorreo(DateTime dia)
        {
            var facturasCorreo = servicio.LeerFacturasDia(dia);
            var listaCorreos = new List<MailMessage>();
            string mailAnterior = "";
            MailMessage mail = new MailMessage();
            foreach (var fra in facturasCorreo)
            {                
                if (fra.Correo != mailAnterior)
                {
                    if (mailAnterior != "")
                    {
                        mail.Subject = mail.Subject.Trim().TrimEnd(',');
                        listaCorreos.Add(mail);
                    }
                    mailAnterior = fra.Correo;
                    ISerieFactura serieFactura;
                    try
                    {
                        serieFactura = LeerSerie(fra.Factura.Substring(0, 2));
                        mail = new MailMessage(serieFactura.CorreoDesdeFactura.Address, fra.Correo);
                        mail.Subject = "Facturación nº ";
                    } catch
                    {
                        mail.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
                        mail.Subject = String.Format("[ERROR: {0}] Facturación nº ", fra.Correo);
                    }
                    
                    mail.Bcc.Add(new MailAddress("carlosadrian@nuevavision.es"));
                    
                    mail.Body = (await GenerarCorreoHTML(fra)).ToString();
                    mail.IsBodyHtml = true;
                }
                mail.Subject += fra.Factura + ", ";
                var facturaPdf = FacturaEnPDF(fra.Empresa, fra.Factura);
                Attachment attachment = new Attachment(new MemoryStream(await facturaPdf.ReadAsByteArrayAsync()), fra.Factura+".pdf");
                mail.Attachments.Add(attachment);
            }
            mail.Subject = mail.Subject.Trim().TrimEnd(',');
            listaCorreos.Add(mail);

            SmtpClient client = new SmtpClient();
            client.Port = 587;
            client.EnableSsl = true;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.UseDefaultCredentials = false;
            string contrasenna = ConfigurationManager.AppSettings["office365password"];
            client.Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", contrasenna);
            client.Host = "smtp.office365.com";

            foreach (var correo in listaCorreos)
            {
                //A veces no conecta a la primera, por lo que reintentamos 2s después
                try
                {
                    client.Send(correo);
                }
                catch
                {
                    await Task.Delay(2000);
                    client.Send(correo);
                }
            }

            return facturasCorreo;
        }
        public List<ClienteCorreoFactura> EnviarFacturasTrimestrePorCorreo(DateTime firstDayOfQuarter, DateTime lastDayOfQuarter)
        {
            List<ClienteCorreoFactura> clientesCorreo = servicio.LeerClientesCorreo(firstDayOfQuarter, lastDayOfQuarter);
            
            bool antesDelError = true;
            int contador = 0;
            
            foreach (var cliente in clientesCorreo)
            {
                
                contador++;
                if (cliente.Cliente.Trim() == "17765" && cliente.Contacto.Trim() == "0")
                {
                    antesDelError = false;
                    continue;
                }
                if (antesDelError)
                {
                    continue;
                }
                
                IEnumerable<FacturaCorreo> facturasCorreo = servicio.LeerFacturasCliente(cliente.Cliente, cliente.Contacto, firstDayOfQuarter, lastDayOfQuarter);

                List<Attachment> facturasAdjuntas = new List<Attachment>();
                foreach (var fra in facturasCorreo)
                {
                    try
                    {
                        using (ByteArrayContent facturaPdf = FacturaEnPDF(fra.Empresa, fra.Factura))
                        {
                            var facturaBytes = facturaPdf.ReadAsByteArrayAsync().Result;
                            Attachment attachment = new Attachment(new MemoryStream(facturaBytes), fra.Factura + ".pdf");
                            facturasAdjuntas.Add(attachment);
                        }
                    } catch
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
                        ISerieFactura serieFactura = LeerSerie(facturasCorreo.First().Factura.Substring(0, 2));
                        mail.From = new MailAddress(serieFactura.CorreoDesdeFactura.Address);
                        mail.Body = GenerarCorreoTrimestreHTML(serieFactura);
                        mail.IsBodyHtml = true;
                        foreach(var adjunta in facturasAdjuntas)
                        {
                            mail.Attachments.Add(adjunta);
                        }
                    }
                    catch
                    {
                        mail.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
                        mail.Subject = String.Format("[ERROR: {0}] Facturas del trimestre", cliente.Correo);
                    }

                    servicio.EnviarCorreoSMTP(mail);
                    mail.Dispose();
                }
            }

            return clientesCorreo;
        }

        private async Task<StringBuilder> GenerarCorreoHTML(FacturaCorreo fra)
        {
            ISerieFactura serieFactura = LeerSerie(fra.Factura.Substring(0, 2));
            StringBuilder s = new StringBuilder();

            s.AppendLine("<p>Adjunto le enviamos su facturación del día.</p>");
            s.AppendLine("<br/>");
            s.AppendLine("<p>La factura se ha generado hoy mismo, por lo que <strong>es lógico que aún no haya recibido los productos</strong>, pero se la adelantamos para que pueda llevar los controles pertinentes.</p>");
            s.AppendLine("<br/>");
            s.AppendLine("<p>¿Qué es la factura electrónica?</p>");
            s.AppendLine("<ul><li>Una factura electrónica es, ante todo, una factura. Es decir, tiene los mismos efectos legales que una factura en papel.</li>");
            s.AppendLine("<li>Recordemos que una factura es un justificante de la entrega de bienes o la prestación de servicios.</li>");
            s.AppendLine("<li>Una factura electrónica es una factura que se expide y recibe en formato electrónico.</li>");
            s.AppendLine("<li>La factura electrónica, por tanto, es una alternativa legal a la factura tradicional en papel.</li></ul>");
            s.AppendLine("<br/>");
            s.AppendLine("<p style=\"color: green;\">Nuestro compromiso con la protección del medio ambiente es firme, por lo que agradecemos que nos ayude a conseguirlo con la eliminación de las facturas en papel.</p>");
            s.AppendLine("<br/>");
            s.AppendLine(serieFactura.FirmaCorreo);

            return s;
        }

        private string GenerarCorreoTrimestreHTML(ISerieFactura serieFactura)
        {
            StringBuilder s = new StringBuilder();

            s.AppendLine("<p>Estimado cliente:</p>");
            s.AppendLine("<p>Adjunto le enviamos todas sus facturas del trimestre para que se las pueda hacer llegar a su gestoría y facilitarle al máximo el trámite trimestral de impuestos.</p>");
            s.AppendLine("<br/>");
            s.AppendLine("<p>¿Qué es la factura electrónica?</p>");
            s.AppendLine("<ul><li>Una factura electrónica es, ante todo, una factura. Es decir, tiene los mismos efectos legales que una factura en papel.</li>");
            s.AppendLine("<li>Recordemos que una factura es un justificante de la entrega de bienes o la prestación de servicios.</li>");
            s.AppendLine("<li>Una factura electrónica es una factura que se expide y recibe en formato electrónico.</li>");
            s.AppendLine("<li>La factura electrónica, por tanto, es una alternativa legal a la factura tradicional en papel.</li></ul>");
            s.AppendLine("<br/>");
            s.AppendLine("<p style=\"color: green;\">Nuestro compromiso con la protección del medio ambiente es firme, por lo que agradecemos que nos ayude a conseguirlo con la eliminación de las facturas en papel.</p>");
            s.AppendLine("<br/>");
            s.AppendLine(serieFactura.FirmaCorreo);

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

        static async Task<string> LeerProductosRecomendados()
        {
            // ESTÁ SIN USAR. 
            // TODO: PONER LOS PRODUCTOS RECOMENDADOS EN EL HTML DEL CORREO DE LA FACTURA
            // OJO, SOLO PARA FACTURA DE NV
            using (var client = new HttpClient())
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
    }
}



public class StringTable
{
    public string[] ColumnNames { get; set; }
    public string[,] Values { get; set; }
}
