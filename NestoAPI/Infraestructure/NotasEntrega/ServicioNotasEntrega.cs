using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>
    /// Servicio para procesar notas de entrega.
    /// Las notas de entrega documentan entregas de productos que pueden estar ya facturados o pendientes de facturación.
    /// </summary>
    public class ServicioNotasEntrega : IServicioNotasEntrega
    {
        private readonly NVEntities db;

        public ServicioNotasEntrega(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Procesa un pedido como nota de entrega.
        ///
        /// Lógica:
        /// - Si líneas NO facturadas (YaFacturado=false): Solo cambia estado a NOTA_ENTREGA (-2), NO toca stock
        /// - Si líneas YA facturadas (YaFacturado=true): Cambia estado a NOTA_ENTREGA (-2) Y da de baja stock
        /// - Obtiene número de nota de entrega de ContadoresGlobales
        /// - Inserta registros en NotasEntrega por cada línea procesada
        /// - Inserta registro en ExtractoRuta con Nº_Orden negativo
        /// </summary>
        /// <param name="pedido">Pedido a procesar como nota de entrega</param>
        /// <param name="usuario">Usuario que realiza la operación</param>
        /// <returns>DTO con los datos de la nota de entrega creada</returns>
        public async Task<NotaEntregaCreadaDTO> ProcesarNotaEntrega(CabPedidoVta pedido, string usuario)
        {
            System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Iniciando procesamiento de nota de entrega para pedido {pedido?.Número}");

            // Validaciones
            if (pedido == null)
            {
                throw new ArgumentNullException(nameof(pedido));
            }

            if (string.IsNullOrWhiteSpace(usuario))
            {
                throw new ArgumentException("El usuario no puede ser null o vacío", nameof(usuario));
            }

            System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Buscando cliente {pedido.Nº_Cliente}/{pedido.Contacto}");
            // Obtener cliente para el nombre
            var cliente = db.Clientes.Find(pedido.Empresa, pedido.Nº_Cliente, pedido.Contacto);

            // Inicializar resultado
            var resultado = new NotaEntregaCreadaDTO
            {
                Empresa = pedido.Empresa,
                NumeroPedido = pedido.Número,
                Cliente = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                NombreCliente = cliente?.Nombre?.Trim() ?? "Desconocido",
                NumeroLineas = 0,
                TeniaLineasYaFacturadas = false,
                BaseImponible = 0m
            };

            // Procesar solo líneas EN_CURSO (estado = 1)
            var lineasAProcesar = pedido.LinPedidoVtas?
                .Where(l => l.Estado == Constantes.EstadosLineaVenta.EN_CURSO)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Líneas a procesar: {lineasAProcesar?.Count ?? 0}");

            if (lineasAProcesar == null || !lineasAProcesar.Any())
            {
                // Sin líneas a procesar, retornar resultado vacío
                System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] No hay líneas EN_CURSO, retornando resultado vacío");
                return resultado;
            }

            // 1. Detectar si hay líneas con YaFacturado=true
            bool hayLineasYaFacturadas = lineasAProcesar.Any(l => l.YaFacturado);
            resultado.TeniaLineasYaFacturadas = hayLineasYaFacturadas;

            // 2. Obtener y actualizar número de nota de entrega y traspaso de ContadoresGlobales
            System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Obteniendo número de nota de entrega y traspaso de ContadoresGlobales");
            var contador = await db.ContadoresGlobales.FirstOrDefaultAsync();
            if (contador == null)
            {
                System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] ERROR: No se encontró ContadoresGlobales");
                throw new InvalidOperationException("No se encontró el registro de ContadoresGlobales");
            }

            int numeroNotaEntrega = contador.NotaEntrega;
            contador.NotaEntrega = numeroNotaEntrega + 1;
            System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Número de nota de entrega asignado: {numeroNotaEntrega}");

            // Obtener y actualizar número de traspaso para PreExtrProducto (si hay líneas ya facturadas)
            int numeroTraspaso = 0;
            if (hayLineasYaFacturadas)
            {
                numeroTraspaso = contador.TraspasoAlmacén;
                contador.TraspasoAlmacén = numeroTraspaso + 1;
                System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Número de traspaso asignado: {numeroTraspaso}");
            }

            // 3. Procesar cada línea
            foreach (var linea in lineasAProcesar)
            {
                // 3a. Insertar en NotasEntrega
                var notaEntregaLinea = new NotaEntrega
                {
                    NºOrden = linea.Nº_Orden,
                    Numero = numeroNotaEntrega,
                    Fecha = DateTime.Now
                };
                db.NotasEntregas.Add(notaEntregaLinea);

                // 3b. Cambiar estado de la línea a NOTA_ENTREGA
                linea.Estado = Constantes.EstadosLineaVenta.NOTA_ENTREGA;

                // 3c. Si YaFacturado=true, dar de baja el stock
                if (linea.YaFacturado)
                {
                    await DarDeBajaStock(pedido, linea, usuario, numeroTraspaso);
                }

                // 3d. Acumular contadores
                resultado.NumeroLineas++;
                resultado.BaseImponible += linea.Base_Imponible;
            }

            // 3. Obtener siguiente Nº_Orden negativo para ExtractoRuta
            System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Obteniendo siguiente Nº_Orden negativo para ExtractoRuta");
            var minOrden = await db.ExtractoRutas
                .Where(e => e.Empresa == pedido.Empresa.Trim())
                .Select(e => (int?)e.Nº_Orden)
                .MinAsync() ?? 0;

            int nuevoOrdenNegativo = minOrden < 0 ? minOrden - 1 : -1;
            System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Nº_Orden negativo asignado: {nuevoOrdenNegativo}");

            // 4. Obtener datos de la primera línea para ExtractoRuta
            var primeraLinea = lineasAProcesar.First();

            // 5. Insertar en ExtractoRuta SOLO si el tipo de ruta lo requiere (Ruta Propia SÍ, Agencia NO)
            var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            if (tipoRuta?.DebeInsertarEnExtractoRuta() == true)
            {
                System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Insertando en ExtractoRuta (Ruta {pedido.Ruta} requiere inserción)");
                var extractoRuta = new ExtractoRuta
                {
                    Empresa = pedido.Empresa,
                    Nº_Orden = nuevoOrdenNegativo,
                    Número = pedido.Nº_Cliente,
                    Contacto = pedido.Contacto,
                    CodPostal = cliente?.CodPostal,
                    Fecha = DateTime.Now,
                    Nº_Documento = numeroNotaEntrega.ToString().PadLeft(10),
                    Efecto = null,
                    Concepto = pedido.Comentarios,
                    Importe = 0,
                    ImportePdte = 0,
                    Delegación = primeraLinea.Delegación,
                    FormaVenta = primeraLinea.Forma_Venta,
                    Vendedor = pedido.Vendedor,
                    FechaVto = null,
                    FormaPago = pedido.Forma_Pago,
                    Ruta = pedido.Ruta,
                    Estado = 0,
                    TipoRuta = Constantes.ExtractoRuta.TIPO_RUTA_PEDIDO,
                    Usuario = usuario,
                    Fecha_Modificación = DateTime.Now
                };
                _ = db.ExtractoRutas.Add(extractoRuta);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] NO se inserta en ExtractoRuta (Ruta {pedido.Ruta} no lo requiere)");
            }

            // 6. Guardar todos los cambios en una única transacción
            System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Guardando cambios en BD (SaveChangesAsync)...");
            try
            {
                _ = await db.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Cambios guardados correctamente. Nota de entrega completada.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] ERROR al guardar cambios: {ex.Message}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] InnerException: {ex.InnerException.Message}");
                    if (ex.InnerException.InnerException != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] InnerException2: {ex.InnerException.InnerException.Message}");
                    }
                }
                throw; // Re-lanzar la excepción para que sea capturada por el gestor
            }

            // 7. Si había líneas ya facturadas, ejecutar prdExtrProducto para reducir el stock
            if (resultado.TeniaLineasYaFacturadas)
            {
                System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] Ejecutando prdExtrProducto para reducir stock (diario={Constantes.DiariosProducto.ENTREGA_FACTURADA})");
                try
                {
                    var empresaParametro = new SqlParameter("@Empresa", SqlDbType.Char, 2)
                    {
                        Value = pedido.Empresa
                    };
                    var diarioParametro = new SqlParameter("@Diario", SqlDbType.Char, 10)
                    {
                        Value = Constantes.DiariosProducto.ENTREGA_FACTURADA
                    };

                    var resultadoProcedimiento = await db.Database.ExecuteSqlCommandAsync(
                        "EXEC prdExtrProducto @Empresa, @Diario",
                        empresaParametro,
                        diarioParametro);

                    System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] prdExtrProducto ejecutado correctamente. Filas afectadas: {resultadoProcedimiento}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"     [ServicioNotasEntrega] ERROR al ejecutar prdExtrProducto: {ex.Message}");
                    throw new Exception($"Error al reducir el stock de productos ya facturados: {ex.Message}", ex);
                }
            }

            return resultado;
        }

        /// <summary>
        /// Da de baja el stock de una línea ya facturada insertando en PreExtrProducto.
        /// El procedimiento prdExtrProducto se ejecutará automáticamente después de guardar todos los cambios.
        /// </summary>
        private async Task DarDeBajaStock(CabPedidoVta pedido, LinPedidoVta linea, string usuario, int numeroTraspaso)
        {
            var preExtr = new PreExtrProducto
            {
                Empresa = pedido.Empresa,
                Número = linea.Producto,
                Fecha = DateTime.Now,
                Nº_Cliente = pedido.Nº_Cliente,
                ContactoCliente = pedido.Contacto,
                Texto = $"Entrega de productos ya facturados pedido {pedido.Número}",
                Almacén = linea.Almacén,
                Grupo = linea.Grupo,
                Cantidad = (short)-linea.Cantidad.GetValueOrDefault(), // NEGATIVO para dar de baja stock
                Importe = linea.Base_Imponible,
                Delegación = linea.Delegación,
                Forma_Venta = linea.Forma_Venta,
                Asiento_Automático = true,
                LinPedido = linea.Nº_Orden,
                NºPedido = pedido.Número,        // Campo Pedido: número del pedido
                NºTraspaso = numeroTraspaso,     // Campo Traspaso: de ContadoresGlobales.TraspasoAlmacén
                Diario = Constantes.DiariosProducto.ENTREGA_FACTURADA,
                Usuario = usuario,
                Fecha_Modificación = DateTime.Now,
                Estado = 0 // Pendiente de procesar
            };

            _ = db.PreExtrProductos.Add(preExtr);

            // Nota: prdExtrProducto se ejecutará automáticamente al finalizar el procesamiento de la nota de entrega
            // para procesar todos los registros de PreExtrProducto con diario _EntregFac y actualizar el stock.
            // El procedimiento usará LinPedido para buscar la ubicación reservada (Ubicaciones.NºOrdenVta)
            // y actualizará su estado automáticamente.
        }
    }
}
