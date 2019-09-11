using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Http;
using Microsoft.Reporting.WebForms;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.Facturas.SeriesFactura;

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

            return new ByteArrayContent(bytes);
        }
        
        public Factura LeerFactura(string empresa, string numeroFactura)
        {
            
            CabFacturaVta cabFactura = servicio.CargarCabFactura(empresa, numeroFactura);
            LinPedidoVta primeraLinea = cabFactura.LinPedidoVtas.FirstOrDefault();

            string claseSerie = "NestoAPI.Models.Facturas.SeriesFactura.Serie" + cabFactura.Serie.Trim();
            Type elementType = Type.GetType(claseSerie);
            ISerieFactura serieFactura = (ISerieFactura)Activator.CreateInstance(elementType);

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

            DireccionFactura direccionEmpresa = new DireccionFactura
            {
                Tipo = "Empresa",
                Nombre = empresaFactura.Nombre?.Trim(),
                CodigoPostal = empresaFactura.CodPostal?.Trim(),
                Direccion = empresaFactura.Dirección?.Trim() + "\n" + empresaFactura.Dirección2?.Trim(),
                Comentarios = empresaFactura.Texto?.Trim(),
                Poblacion = empresaFactura.Población?.Trim(),
                Provincia = empresaFactura.Provincia?.Trim(),
                Telefonos = empresaFactura.Teléfono?.Trim()
            };

            DireccionFactura direccionRazonSocial = new DireccionFactura
            {
                Tipo = "Fiscal",
                Nombre = clienteRazonSocial.Nombre?.Trim(),
                CodigoPostal = clienteRazonSocial.CodPostal?.Trim(),
                Direccion = clienteRazonSocial.Dirección?.Trim(),
                Poblacion = clienteRazonSocial.Población?.Trim(),
                Provincia = clienteRazonSocial.Provincia?.Trim(),
                Telefonos = clienteRazonSocial.Teléfono?.Trim()
            };

            DireccionFactura direccionEntrega = new DireccionFactura
            {
                Tipo = "Entrega",
                Nombre = clienteEntrega.Nombre?.Trim(),
                CodigoPostal = clienteEntrega.CodPostal?.Trim(),
                Direccion = clienteEntrega.Dirección?.Trim(),
                Poblacion = clienteEntrega.Población?.Trim(),
                Provincia = clienteEntrega.Provincia?.Trim(),
                Telefonos = clienteEntrega.Teléfono?.Trim()
            };

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
                    Producto = linea.Producto?.Trim()
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
                    ImporteRecargoEquivalencia = Math.Round(lineasGrupo.Sum(l => l.ImporteRE), 2, MidpointRounding.AwayFromZero),
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
                } else
                {
                    decimal totalAcumulado = 0;
                    int i = 0;
                    
                    while (i < vencimientos.Count)
                    {
                        totalAcumulado += vencimientos[i].Importe;
                        if (totalAcumulado == 0)
                        {
                            vencimientos.RemoveRange(0, i+1);
                            i = 0;
                        } else
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

            foreach(var vencimiento in vencimientos)
            {
                if (vencimiento.CCC != null && vencimiento.CCC.Trim() != "")
                {
                    continue;
                }

                if (vencimiento.FormaPago == "TRN")
                {
                    vencimiento.Iban = servicio.CuentaBancoEmpresa(empresa);
                } else
                {
                    vencimiento.Iban = "<<< No Procede >>>";
                }                
            }

            Factura factura = new Factura
            {
                Cliente = cabFactura.Nº_Cliente.Trim(),
                Comentarios = cabPedido?.Comentarios?.Trim(),
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
                Totales = totales,
                Vencimientos = vencimientos.OrderBy(v => v.Vencimiento).ToList(),
                Vendedores = vendedores
            };

            return factura;
        }

        public List<Factura> LeerFacturas(List<FacturaLookup> numerosFactura)
        {
            List<Factura> facturas = new List<Factura>();

            foreach (var factura in numerosFactura)
            {
                Factura nuevaFactura = LeerFactura(factura.Empresa, factura.Factura);
                facturas.Add(nuevaFactura);
            }

            return facturas;
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
    }
}