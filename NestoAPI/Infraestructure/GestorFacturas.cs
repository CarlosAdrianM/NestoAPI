using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Infraestructure
{
    public class GestorFacturas : IGestorFacturas
    {
        IServicioFacturas servicio;

        public GestorFacturas(IServicioFacturas servicio)
        {
            this.servicio = servicio;
        }

        public Factura LeerFactura(string empresa, string numeroFactura)
        {
            
            CabFacturaVta cabFactura = servicio.CargarCabFactura(empresa, numeroFactura);
            LinPedidoVta primeraLinea = cabFactura.LinPedidoVtas.FirstOrDefault();

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
                Nombre = clienteRazonSocial.Nombre?.Trim(),
                CodigoPostal = clienteRazonSocial.CodPostal?.Trim(),
                Direccion = clienteRazonSocial.Dirección?.Trim(),
                Poblacion = clienteRazonSocial.Población?.Trim(),
                Provincia = clienteRazonSocial.Provincia?.Trim(),
                Telefonos = clienteRazonSocial.Teléfono?.Trim()
            };

            DireccionFactura direccionEntrega = new DireccionFactura
            {
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
            foreach (LinPedidoVta linea in cabFactura.LinPedidoVtas)
            {
                LineaFactura lineaNueva = new LineaFactura
                {
                    Albaran = (int)linea.Nº_Albarán,
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

            List<string> notas = new List<string>
            {
                "EL PLAZO MÁXIMO PARA CUALQUIER RECLAMACIÓN DE ESTE PEDIDO ES DE 24 HORAS.",
                "LOS GASTOS POR DEVOLUCIÓN DEL PRODUCTO SERÁN SIEMPRE A CARGO DEL CLIENTE."
            };

            List<TotalFactura> totales = new List<TotalFactura>();
            var gruposTotal = cabFactura.LinPedidoVtas.GroupBy(l => new { l.PorcentajeIVA, l.PorcentajeRE });
            foreach (var grupoTotal in gruposTotal)
            {
                var lineasGrupo = cabFactura.LinPedidoVtas.Where(l => l.PorcentajeIVA == grupoTotal.Key.PorcentajeIVA && l.PorcentajeRE == grupoTotal.Key.PorcentajeRE);
                TotalFactura total = new TotalFactura
                {
                    BaseImponible = lineasGrupo.Sum(l => l.Base_Imponible),
                    ImporteIVA = Math.Round(lineasGrupo.Sum(l => l.ImporteIVA), 2),
                    ImporteRecargoEquivalencia = Math.Round(lineasGrupo.Sum(l => l.ImporteRE), 2),
                    PorcentajeIVA = grupoTotal.Key.PorcentajeIVA / 100M,
                    PorcentajeRecargoEquivalencia = grupoTotal.Key.PorcentajeRE
                };
                totales.Add(total);
            }

            decimal importeTotal = Math.Round(cabFactura.LinPedidoVtas.Sum(l => l.Total), 2);

            List<string> vendedores = servicio.CargarVendedoresFactura(empresa, numeroFactura);
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
            
            Factura factura = new Factura
            {
                Cliente = cabFactura.Nº_Cliente.Trim(),
                Comentarios = cabPedido?.Comentarios?.Trim(),
                Delegacion = primeraLinea.Delegación?.Trim(),
                Direcciones = direcciones,
                DatosRegistrales = empresaFactura.TextoFactura?.Trim(),
                Fecha = cabFactura.Fecha,
                LogoURL = String.Format("{0}\\{1}_factura", empresaFactura.Logotipo?.Trim(), empresa.ToString()),
                ImporteTotal = importeTotal,
                Lineas = lineas,
                Nif = clienteRazonSocial.CIF_NIF?.Trim(),
                NotasAlPie = notas,
                NumeroFactura = cabFactura.Número?.Trim(),
                Ruta = cabPedido.Ruta?.Trim(),
                Totales = totales,
                Vencimientos = vencimientos,
                Vendedores = vendedores
            };

            return factura;
        }
    }
}