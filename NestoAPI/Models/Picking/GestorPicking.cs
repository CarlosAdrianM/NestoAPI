using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Models.Picking
{
    public class GestorPicking
    {
        private ModulosPicking modulos;
        private List<PedidoPicking> candidatos;
        private List<PedidoPicking> retenidosPorPrepago;
        // NestoAPI#362: pedidos retirados porque la entrega caería en día que el cliente cierra
        private List<PedidoPicking> sinSalirPorCierreCliente;
        private DateTime diaEntregaPicking;
        private bool ignorarCierreCliente;
        // NestoAPI#608: el pedido del picking de UN pedido (null en los de cliente y rutas), para explicar por qué no sale
        private int? pedidoUnico;
        private string empresaPedidoUnico;
        private NVEntities db = new NVEntities();

        public GestorPicking(ModulosPicking modulos)
        {
            this.modulos = modulos;
        }
        /// <summary>
        /// Picking interactivo: el horizonte de entrega se DEDUCE de la hora, como siempre.
        /// </summary>
        public void SacarPicking()
        {
            SacarPicking(FechaPickingAhora(Constantes.Empresas.EMPRESA_POR_DEFECTO));
        }

        /// <summary>
        /// NestoAPI#361: picking con el horizonte de entrega COMO DATO, no deducido del reloj.
        ///
        /// El horizonte decide hasta qué fecha de entrega se sirve
        /// (<c>BorrarLineasEntregaFutura</c> quita las líneas con FechaEntrega mayor), y hasta
        /// ahora salía siempre de <c>CalcularFechaPicking(DateTime.Now)</c>. Eso hacía que el
        /// picking de cierre de las 11h fuera peligrosamente sensible al segundo exacto en que
        /// arrancara: a las 10:59:59 servía HOY, y a las 11:00:01 pasaba a servir también lo de
        /// MAÑANA, adelantando un día las entregas sin que nadie se enterase. Se toreaba
        /// programando la tarea a las 10:59:40, a costa de dejar fuera los pedidos metidos en
        /// esos últimos 20 segundos (que el propio PedidosVentaController sí permite meter,
        /// porque su corte son las 11h en punto).
        ///
        /// El picking de cierre no necesita preguntarle la hora a nadie: ya sabe que sirve para
        /// hoy. Pasándolo como dato, da igual que la tarea arranque a las 11:00:00, a las
        /// 11:00:30 o tarde por lo que sea.
        /// </summary>
        /// <param name="fechaPicking">Fecha de entrega hasta la que se sirve en este picking.</param>
        public void SacarPicking(DateTime fechaPicking)
        {
            EnExclusiva(() =>
            {
                candidatos = modulos.rellenadorPicking.Rellenar();
                Ejecutar(fechaPicking);
            });
        }

        public void SacarPicking(List<Ruta> rutas)
        {
            EnExclusiva(() =>
            {
                candidatos = modulos.rellenadorPicking.Rellenar(rutas);
                Ejecutar(FechaPickingAhora(Constantes.Empresas.EMPRESA_POR_DEFECTO));
            });
        }

        /// <param name="ignorarCierreCliente">El usuario ha confirmado «¿Aún así quieres asignarle picking?» después de
        /// que el pedido no saliera porque el cliente cierra el día de la entrega (01/10/26). Solo en el picking de UN
        /// pedido: en los de cliente y de rutas la regla se aplica siempre.</param>
        public void SacarPicking(string empresa, int numeroPedido, bool ignorarCierreCliente = false)
        {
            this.ignorarCierreCliente = ignorarCierreCliente;
            pedidoUnico = numeroPedido;
            empresaPedidoUnico = empresa;
            EnExclusiva(() =>
            {
                candidatos = modulos.rellenadorPicking.Rellenar(empresa, numeroPedido);
                Ejecutar(FechaPickingAhora(empresa));
            });
        }

        public void SacarPicking(string cliente)
        {
            EnExclusiva(() =>
            {
                candidatos = modulos.rellenadorPicking.Rellenar(cliente);
                Ejecutar(FechaPickingAhora(Constantes.Empresas.EMPRESA_POR_DEFECTO));
            });
        }

        // NestoAPI#405: el picking NO era idempotente frente a dos ejecuciones solapadas.
        //
        // Entre el Rellenar() (que lee las líneas con Picking null) y el SaveChanges del
        // finalizador pasan segundos: reserva de stock, portes, pendientes y ubicaciones. Dos
        // peticiones que entren dentro de esa ventana leen AMBAS las mismas líneas como
        // disponibles y las procesan las dos. El número de picking de la línea se pisa (gana el
        // último UPDATE) y no se nota, pero las ubicaciones NO se pisan: cada pasada reserva la
        // suya, y la línea acaba con el DOBLE de unidades ubicadas. Como el SP del packing suma
        // las ubicaciones de cada línea, la hoja sale con el doble y el almacén serviría de más.
        //
        // Pasó el 25/08/2026 con el picking 99327 (pedidos 924333, 924798 y 924799): la huella
        // fue un número de picking consumido y sin usar, el 99326.
        //
        // Toda la ejecución pasa a ser sección crítica, con el mismo applock de #294. Se libera
        // en el finally, y como los SaveChanges van en autocommit, cuando la segunda entra ya ve
        // las líneas con su picking asignado y el filtro del rellenador las deja fuera.
        private const string RECURSO_BLOQUEO = "Picking:SacarPicking";
        private const int TIMEOUT_BLOQUEO_MS = 120000;  // el picking de cierre es largo

        private void EnExclusiva(Action accion)
        {
            System.Data.Common.DbConnection conexion = db.Database.Connection;
            // El applock de ámbito Session vive mientras viva la CONEXIÓN, así que hay que
            // abrirla a mano: si se deja al pool, EF la devuelve entre operaciones y el bloqueo
            // se soltaría a mitad de picking.
            bool laAbrimosAqui = conexion.State != System.Data.ConnectionState.Open;
            if (laAbrimosAqui)
            {
                conexion.Open();
            }
            try
            {
                _ = db.Database.ExecuteSqlCommand(
                    @"DECLARE @resultado int;
                      EXEC @resultado = sp_getapplock @Resource = @p0, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @p1;
                      IF @resultado < 0 RAISERROR('Ya se está sacando otro picking en este momento. Espere a que termine e inténtelo de nuevo.', 16, 1);",
                    RECURSO_BLOQUEO, TIMEOUT_BLOQUEO_MS);
                try
                {
                    accion();
                }
                finally
                {
                    _ = db.Database.ExecuteSqlCommand(
                        "EXEC sp_releaseapplock @Resource = @p0, @LockOwner = 'Session';", RECURSO_BLOQUEO);
                }
            }
            finally
            {
                if (laAbrimosAqui)
                {
                    conexion.Close();
                }
            }
        }


        public List<PedidoPicking> PedidosEnPicking()
        {
            return candidatos;
        }

        private void Ejecutar(DateTime fechaPicking)
        {
            List<StockProducto> stocks;
            List<LineaPedidoPicking> todasLasLineas;

            stocks = modulos.rellenadorStocks.Rellenar(candidatos);

            todasLasLineas = modulos.rellenadorPicking.RellenarTodasLasLineas(candidatos);

            // NestoAPI#482 (modo 3): el pool ANTES de repartir, porque Reservar consume los stocks.
            List<StockProducto> stocksIniciales = stocks.Select(st => st.Clonar()).ToList();

            GestorReservasStock.Reservar(stocks, candidatos, todasLasLineas);

            GestorReposicionTiendas.MarcarEsperas(stocksIniciales, candidatos, todasLasLineas);

            GestorReservasStock.BorrarLineasQueNoDebenSalir(candidatos, fechaPicking);

            // NestoAPI#362: si la entrega (el laborable siguiente a la salida) cae en un día que
            // el cliente cierra, el pedido no sale en esta pasada; se reevalúa en la siguiente.
            DateTime diaEntrega = GestorDiasEnServir.CalcularDiaEntrega(fechaPicking,
                f => GestorFestivos.EsFestivo(f, Constantes.Almacenes.ALGETE));
            // NestoAPI#588: si la ruta del pedido ya es propia, se entrega el laborable siguiente a hoy (no el siguiente
            // a la salida de la agencia)
            DateTime diaEntregaRutaPropia = GestorDiasEnServir.CalcularDiaEntregaRutaPropia(DateTime.Today, fechaPicking,
                f => GestorFestivos.EsFestivo(f, Constantes.Almacenes.ALGETE));
            sinSalirPorCierreCliente = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(candidatos, diaEntrega, diaEntregaRutaPropia, ignorarCierreCliente);
            diaEntregaPicking = diaEntrega;

            // NestoAPI#542: los pedidos que se facturan «todo ahora» convierten lo que falta en Recoger, antes
            // de que las reglas de abajo decidan qué sale.
            GestorFacturarTodoAhora.Aplicar(candidatos);

            // Recorrer Candidatos (quitamos los que no tienen que salir)
            for (int i = 0; i < candidatos.Count(); i++)
            {
                PedidoPicking pedido = candidatos[i];
                if (!DecidirSiSale(pedido))
                {
                    pedido.Borrar = true;
                    // NestoAPI#608: el motivo, también en los picking de varios pedidos
                    System.Diagnostics.Trace.WriteLine($"[Picking#608] Pedido {pedido.Id} no sale: {pedido.MotivoNoSale}");
                }
                else
                {
                    if (pedido.hayQueSumarPortes())
                    {
                        GeneradorPortes generadorPortes = new GeneradorPortes(db, pedido);
                        generadorPortes.Ejecutar();
                    };
                }
            }

            // Actualizar Pendientes
            GeneradorPendientes generadorPendientes = new GeneradorPendientes(db, candidatos);
            generadorPendientes.Ejecutar();

            retenidosPorPrepago = candidatos.Where(c => c.RetenidoPorPrepago).ToList();
            List<PedidoPicking> descartados = candidatos.Where(c => c.Borrar).ToList();
            candidatos.RemoveAll(c => c.Borrar);

            // Asignar Picking
            AsignadorPicking asignadorPicking = new AsignadorPicking(db, candidatos);
            asignadorPicking.Ejecutar();

            // Finalizar Picking
            modulos.finalizador.Ejecutar(db);

            // Si no se ha asignado picking a nada, damos error de NEGOCIO (400, no 500):
            // es un resultado esperable (sin stock o nada que sacar), no un fallo del sistema.
            // 01/10/26: si lo que lo ha dejado vacío es el cierre del cliente, se dice (y el correo de #362, que iba
            // después de este punto, sale antes de cortar).
            if (candidatos.Count == 0)
            {
                if (sinSalirPorCierreCliente.Count > 0)
                {
                    try
                    {
                        GestorDiasEnServir.EnviarCorreo(sinSalirPorCierreCliente, diaEntregaPicking);
                    }
                    catch (Exception)
                    {
                        // Un fallo de correo no debe tapar el motivo
                    }
                }
                throw ErrorSinPicking(descartados);
            }

            // Mandamos el correo con los pedidos que van por debajo del margen
            GestorMargenes gestor = new GestorMargenes();
            gestor.Rellenar(asignadorPicking.numeroPicking);
            gestor.enviarCorreo();
            GestorPrepagos.EnviarCorreo(retenidosPorPrepago);
            // NestoAPI#362: un pedido que no sale sin decir por qué parece un cuelgue
            GestorDiasEnServir.EnviarCorreo(sinSalirPorCierreCliente, diaEntregaPicking);
            // NestoAPI#253: aviso con importe a vendedor y usuario para los pedidos con la casilla
            // marcada. Nunca lanza (un fallo de correo no debe romper el picking).
            GestorAvisosPicking.EnviarCorreos(candidatos,
                vendedor => db.Vendedores.FirstOrDefault(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && v.Número == vendedor)?.Mail?.Trim());
            // NestoAPI#555: y a la campana de Nesto / push de NestoApp del usuario del pedido, en segundo
            // plano (la push va por Firebase y no debe alargar el picking, que corre en exclusiva).
            List<PedidoPicking> avisables = candidatos?.Where(c => c != null && c.AvisarConImporteAlCogerPicking).ToList();
            if (avisables != null && avisables.Any())
            {
                _ = System.Threading.Tasks.Task.Run(() => GestorAvisosPicking.AvisarEnAplicaciones(avisables,
                    new Infraestructure.Notificaciones.ServicioNotificacionesPush()));
            }
            
        }

        /// <summary>
        /// La decisión de siempre (saleEnPicking, que le quede alguna línea y que haya stock de algo), dejando en
        /// <see cref="PedidoPicking.MotivoNoSale"/> por qué no sale (NestoAPI#608). No cambia ninguna regla.
        /// </summary>
        internal static bool DecidirSiSale(PedidoPicking pedido)
        {
            bool sale = Decidir(pedido);
            pedido.LineasQueFaltanAlDecidir = sale ? null : ExplicadorPedidoSinPicking.LineasQueFaltan(pedido);
            return sale;
        }

        private static bool Decidir(PedidoPicking pedido)
        {
            if (!pedido.saleEnPicking())
            {
                return false;
            }
            if (pedido.Lineas.Count == 0)
            {
                pedido.MotivoNoSale = MotivoNoSalePicking.SinLineas;
                return false;
            }
            if (!new GestorStocksPicking(pedido).HayStockDeAlgo())
            {
                pedido.MotivoNoSale = MotivoNoSalePicking.SinStockDePago;
                return false;
            }
            return true;
        }

        /// <summary>
        /// El error cuando no sale nada. Manda el cierre del cliente (01/10/26); si no, en el picking de UN pedido,
        /// el motivo concreto (NestoAPI#608); si no, el genérico de stock.
        /// </summary>
        private Infraestructure.Exceptions.NestoBusinessException ErrorSinPicking(List<PedidoPicking> descartados)
        {
            if (sinSalirPorCierreCliente.Count == 0 && pedidoUnico.HasValue)
            {
                PedidoPicking pedido = descartados.FirstOrDefault(p => p.Id == pedidoUnico.Value);
                if (pedido != null)
                {
                    try
                    {
                        List<string> productos = ExplicadorPedidoSinPicking.ProductosANombrar(pedido);
                        var error = ExplicadorPedidoSinPicking.Error(pedido,
                            NombresProductos(empresaPedidoUnico, productos), ComprasPendientes(empresaPedidoUnico, productos));
                        if (error != null)
                        {
                            return error;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Explicar es cortesía: si falla la consulta, el mensaje de siempre
                        System.Diagnostics.Trace.WriteLine($"[Picking#608] No se pudo explicar el pedido {pedido.Id}: {ex.Message}");
                    }
                }
            }
            return GestorDiasEnServir.ErrorSinPicking(sinSalirPorCierreCliente, diaEntregaPicking);
        }

        private Dictionary<string, string> NombresProductos(string empresa, List<string> productos)
        {
            if (productos.Count == 0)
            {
                return new Dictionary<string, string>();
            }
            return db.Productos
                .Where(p => p.Empresa == empresa && productos.Contains(p.Número))
                .Select(p => new { p.Número, p.Nombre })
                .ToList()
                .GroupBy(p => p.Número.Trim())
                .ToDictionary(g => g.Key, g => g.First().Nombre?.Trim());
        }

        /// <summary>El primer pedido a proveedor enviado y sin recibir de cada producto (mismo filtro que la sombra del modo de servicio).</summary>
        private Dictionary<string, CompraPendientePicking> ComprasPendientes(string empresa, List<string> productos)
        {
            if (productos.Count == 0)
            {
                return new Dictionary<string, CompraPendientePicking>();
            }
            string espejo = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;
            return db.LinPedidoCmps
                .Where(c => (c.Empresa == empresa || c.Empresa == espejo) && productos.Contains(c.Producto)
                    && (c.Estado == Constantes.EstadosLineaVenta.PENDIENTE || c.Estado == Constantes.EstadosLineaVenta.EN_CURSO) && c.Enviado)
                .Select(c => new { c.Producto, c.Número, c.FechaRecepción })
                .ToList()
                .GroupBy(c => c.Producto.Trim())
                .ToDictionary(g => g.Key, g =>
                {
                    var primera = g.OrderBy(c => c.FechaRecepción ?? DateTime.MaxValue).ThenBy(c => c.Número).First();
                    return new CompraPendientePicking { Pedido = primera.Número, FechaPrevista = primera.FechaRecepción };
                });
        }

        /// <summary>Horizonte del picking interactivo según el reloj y la hora de corte de la empresa.</summary>
        private static DateTime FechaPickingAhora(string empresa)
        {
            return CalcularFechaPicking(DateTime.Now, HoraCortePicking.Leer(empresa));
        }

        /// <summary>
        /// NestoAPI#361: ¿el instante dado está ya pasado el corte del día? Se extrae para poder
        /// testear el límite exacto, que antes vivía enterrado en una comparación con
        /// DateTime.Now y era intestable sin congelar el reloj. El corte es EN PUNTO: con corte a
        /// las 11:00, a las 10:59:59 todavía se sirve hoy. NestoAPI#577: la hora de corte ya no es
        /// un 11 fijo, sale de <see cref="HoraCortePicking.Leer"/>.
        /// </summary>
        internal static bool CorteDelDiaSuperado(DateTime instante, TimeSpan horaCorte)
        {
            return instante.TimeOfDay >= horaCorte;
        }

        /// <summary>
        /// Deduce el horizonte de entrega a partir de la hora. Lo usa el picking INTERACTIVO; el
        /// de cierre recibe el horizonte como dato (ver SacarPicking(DateTime)).
        /// </summary>
        internal static DateTime CalcularFechaPicking(DateTime fechaConHora, TimeSpan horaCorte)
        {
            DateTime fechaSinHora = new DateTime(fechaConHora.Year, fechaConHora.Month, fechaConHora.Day);

            // Si es antes del corte devuelve la fecha de hoy (sin hora)
            if (!CorteDelDiaSuperado(fechaConHora, horaCorte))
            {
                return fechaSinHora;
            }

            // Si es después del corte devolvemos el siguiente día laboral            
            var fechaDevolver = fechaSinHora.AddDays(1);
            while (GestorFestivos.EsFestivo(fechaDevolver, Constantes.Almacenes.ALGETE))
            {
                fechaDevolver = fechaDevolver.AddDays(1);
            }

            return fechaDevolver;
        }        
    }        
}