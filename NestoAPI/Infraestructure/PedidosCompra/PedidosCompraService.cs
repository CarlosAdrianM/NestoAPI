using NestoAPI.Models.PedidosCompra;
using NestoAPI.Models;
using System;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System.Data;
using System.Data.Entity;
using NestoAPI.Infraestructure.Contabilidad;

namespace NestoAPI.Infraestructure.PedidosCompra
{
    public class PedidosCompraService : IPedidosCompraService
    {
        public async Task<int> CrearAlbaran(int pedidoId, NVEntities db, string usuario = null)
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            DateTime fechaRecepcion = DateTime.Now;
            decimal importeMinimo = 0M;
            bool contabiliza = true;

            var empresaParametro = new SqlParameter("@Empresa", SqlDbType.Char, 3)
            {
                Value = empresa
            };

            var pedidoParametro = new SqlParameter("@Pedido", SqlDbType.Int)
            {
                Value = pedidoId
            };

            var fechaRecepcionParametro = new SqlParameter("@FechaRecepción", SqlDbType.DateTime)
            {
                Value = fechaRecepcion
            };

            var importeMinimoParametro = new SqlParameter("@ImporteMínimo", SqlDbType.Money)
            {
                Value = importeMinimo
            };

            var contabilizaParametro = new SqlParameter("@Contabiliza", SqlDbType.Bit)
            {
                Value = contabiliza
            };
            
            var usuarioParametro = new SqlParameter("@Usuario", SqlDbType.Char, 30)
            {
                Value = usuario
            };
            

            var resultadoParametro = new SqlParameter
            {
                ParameterName = "@Resultado",
                SqlDbType = SqlDbType.Int,
                Direction = ParameterDirection.Output // Configurar para capturar el valor de retorno
            };
            try
            {
                // Ejecutar el procedimiento almacenado y capturar el valor de retorno
                var resultadoDirecto = await db.Database.ExecuteSqlCommandAsync("EXEC @Resultado = prdCrearAlbaránCmp @Empresa, @Pedido, @FechaRecepción, @ImporteMínimo, @Contabiliza, @Usuario",
                    resultadoParametro, empresaParametro, pedidoParametro, fechaRecepcionParametro, importeMinimoParametro, contabilizaParametro, usuarioParametro);
                // Obtener el valor de retorno del parámetro
                var numeroAlbaran = (int)resultadoParametro.Value;
                return numeroAlbaran;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al contabilizar el diario", ex);
            }
        }

        public async Task<CrearFacturaCmpResponse> CrearAlbaranYFactura(int pedidoId, DateTime fecha, NVEntities db, string usuario = null)
        {
            int albaran = await CrearAlbaran(pedidoId, db, usuario);
            if (albaran == 0)
            {
                throw new Exception("No se ha podido crear el albarán");
            }

            CrearFacturaCmpResponse factura = await CrearFactura(pedidoId, fecha, db, usuario);

            return factura;
        }

        public async Task<CrearFacturaCmpResponse> CrearFactura(int pedidoId, DateTime fecha, NVEntities db, string usuario = null)
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string numFactura = string.Empty;
            bool contabiliza = true;

            var empresaParametro = new SqlParameter("@Empresa", SqlDbType.Char, 3)
            {
                Value = empresa
            };

            var pedidoParametro = new SqlParameter("@Pedido", SqlDbType.Int)
            {
                Value = pedidoId
            };

            var fechaParametro = new SqlParameter("@Fecha", SqlDbType.DateTime)
            {
                Value = fecha
            };

            var numFacturaParametro = new SqlParameter("@NumFactura", SqlDbType.Char, 10)
            {
                Value = numFactura
            };

            var usuarioParametro = new SqlParameter("@Usuario", SqlDbType.Char, 30)
            {
                Value = usuario
            };

            var resultadoParametro = new SqlParameter
            {
                ParameterName = "@Resultado",
                SqlDbType = SqlDbType.Int,
                Direction = ParameterDirection.Output // Configurar para capturar el valor de retorno
            };

            try
            {
                // Ejecutar el procedimiento almacenado y capturar el valor de retorno
                var resultadoDirecto = await db.Database.ExecuteSqlCommandAsync("EXEC @Resultado = prdCrearFacturaCmp @Empresa, @Pedido, @Fecha, @NumFactura, @Usuario",
                    resultadoParametro, empresaParametro, pedidoParametro, fechaParametro, numFacturaParametro, usuarioParametro);

                // Obtener el valor de retorno del parámetro
                var resultadoProcedimiento = (int)resultadoParametro.Value;
                
                CrearFacturaCmpResponse respuesta = new CrearFacturaCmpResponse();
                respuesta.Exito = resultadoProcedimiento >= 0;
                respuesta.Pedido = pedidoId;
                respuesta.Factura = resultadoProcedimiento;
                var factura = await db.CabFacturasCmp.SingleAsync(f => f.Número == respuesta.Factura.ToString());
                var extracto = await db.ExtractosProveedor
                    .SingleAsync(e => e.Empresa == empresa && e.Número == factura.NºProveedor && e.TipoApunte == Constantes.TiposExtractoCliente.FACTURA && e.NºDocumento == respuesta.Factura.ToString());
                respuesta.AsientoFactura = extracto.Asiento;
                respuesta.ImporteFactura = extracto.Importe;
                respuesta.ExtractoProveedorCarteraId = (await db.ExtractosProveedor
                    .FirstAsync(e => e.Empresa == empresa && e.ImportePdte != 0 && e.TipoApunte == Constantes.TiposExtractoCliente.CARTERA && e.NºDocumento == respuesta.Factura.ToString() && e.Asiento == respuesta.AsientoFactura))
                    .NºOrden;
                return respuesta;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al contabilizar el diario", ex);
            }
        }

        public async Task<int> CrearPagoFactura(CrearFacturaCmpRequest request, CrearFacturaCmpResponse respuesta, NVEntities db)
        {
            try
            {
                string textoDocumento = string.IsNullOrEmpty(request.Documento) ? string.Empty : $"({request.Documento})";
                PreContabilidad preContabilidad = new PreContabilidad
                {
                    Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Diario = Constantes.DiariosContables.COMISIONES_BANCO,
                    TipoApunte = Constantes.TiposExtractoCliente.PAGO,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.PROVEEDOR,
                    Nº_Cuenta = request.Pedido.Proveedor,
                    Contacto = request.Pedido.Contacto,
                    Asiento = 1,
                    Fecha = request.Pedido.Fecha,
                    Debe = respuesta.ImporteFactura > 0 ? respuesta.ImporteFactura : 0,
                    Haber = respuesta.ImporteFactura < 0 ? -respuesta.ImporteFactura : 0,
                    Concepto = $"N/Pago S/Fra.{request.Pedido.FacturaProveedor} - {respuesta.Factura} {textoDocumento}",
                    NºDocumentoProv = request.Pedido.FacturaProveedor,
                    Nº_Documento = respuesta.Factura.ToString(),
                    Liquidado = respuesta.ExtractoProveedorCarteraId,
                    Contrapartida = request.ContraPartidaPago,
                    Delegación = request.Pedido.Lineas.First().Delegacion,
                    FormaVenta = Constantes.Empresas.FORMA_VENTA_POR_DEFECTO,
                    Usuario = request.Pedido.Usuario
                };
                ContabilidadService servicio = new ContabilidadService();
                int asiento = await servicio.CrearLineasYContabilizarDiario(new System.Collections.Generic.List<PreContabilidad> { preContabilidad }, db).ConfigureAwait(false);
                return asiento;
            }
            catch (Exception ex)
            {
                throw new Exception("No se ha podido crear el pago de la factura", ex);
            }
        }

        public async Task<CabPedidoCmp> CrearPedido(PedidoCompraDTO pedido, NVEntities db)
        {
            // El número que vamos a dar al pedido hay que leerlo de ContadoresGlobales
            ContadorGlobal contador = db.ContadoresGlobales.SingleOrDefault();
            if (pedido.Id == 0)
            {
                contador.PedidosCmp++;
                pedido.Id = contador.PedidosCmp;
            }

            CabPedidoCmp cabecera = pedido.ToCabPedidoCmp();

            db.CabPedidosCmp.Add(cabecera);

            try
            {
                await db.SaveChangesAsync();
                return cabecera;
            }
            catch (DbUpdateException ex)
            {
                throw new DbUpdateException("Se ha producido un error de base de datos al crear el pedido", ex);
            }
            catch (Exception ex)
            {
                throw new Exception("Se ha producido un error al crear el pedido", ex);
            }
        }
    }
}