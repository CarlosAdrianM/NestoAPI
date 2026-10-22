using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosCliente;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#612: EventosInscripcionesCurso por SQL directo (sin EDMX). Sin la tabla (script sin lanzar) no hay nada
    /// que leer, y escribir falla: por eso todo va detrás del interruptor <see cref="InterruptoresEventos.CLAVE_FACTURAR_CURSO"/>.
    /// </summary>
    public class RepositorioFacturacionCursos : IRepositorioFacturacionCursos
    {
        /// <summary>Si un proceso se cae a medias, a los 15 minutos otro puede seguir.</summary>
        internal const int MINUTOS_RECLAMO = 15;

        private const string COLUMNAS = @"InscripcionId, RTRIM(EmpresaPedido) AS EmpresaPedido, Pedido, RTRIM(EmpresaFactura) AS EmpresaFactura,
       RTRIM(Factura) AS Factura, EstadoLiquidacion, NumOrdenCobro, NumOrdenEfecto, Estado, Mensaje, Intentos, Usuario, FechaCreacion, FechaModificacion";

        internal const string SQL_LEER = @"
IF OBJECT_ID('dbo.EventosInscripcionesCurso') IS NOT NULL
    EXEC sp_executesql N'SELECT " + COLUMNAS + @" FROM dbo.EventosInscripcionesCurso WHERE InscripcionId = @id', N'@id int', @id = @p0
ELSE
    SELECT CAST(0 AS int) AS InscripcionId, CAST(NULL AS varchar(3)) AS EmpresaPedido, CAST(NULL AS int) AS Pedido,
           CAST(NULL AS varchar(3)) AS EmpresaFactura, CAST(NULL AS varchar(10)) AS Factura, CAST(NULL AS varchar(20)) AS EstadoLiquidacion,
           CAST(NULL AS int) AS NumOrdenCobro, CAST(NULL AS int) AS NumOrdenEfecto, CAST(NULL AS varchar(20)) AS Estado,
           CAST(NULL AS nvarchar(1000)) AS Mensaje, 0 AS Intentos, CAST(NULL AS varchar(50)) AS Usuario, GETDATE() AS FechaCreacion,
           GETDATE() AS FechaModificacion
    WHERE 1 = 0";

        // La fila nace al reclamar; el reclamo es un UPDATE condicionado (dos procesos a la vez: solo uno pasa).
        // El INSERT choca con la PK si otro proceso la crea a la vez (se ignora: la fila ya está). Lo que decide es el
        // UPDATE condicionado; su @@ROWCOUNT es la respuesta.
        internal const string SQL_RECLAMAR = @"
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM dbo.EventosInscripcionesCurso WHERE InscripcionId = @p0)
BEGIN TRY
    INSERT INTO dbo.EventosInscripcionesCurso (InscripcionId, EmpresaPedido, Estado, Intentos, Usuario, FechaCreacion, FechaModificacion)
    VALUES (@p0, @p1, 'EnCurso', 0, @p2, GETDATE(), GETDATE());
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
END CATCH;
UPDATE dbo.EventosInscripcionesCurso
SET ProcesandoDesde = GETDATE()
WHERE InscripcionId = @p0 AND (ProcesandoDesde IS NULL OR ProcesandoDesde < DATEADD(minute, -15, GETDATE()));
SELECT @@ROWCOUNT;";

        internal const string SQL_SOLTAR = "UPDATE dbo.EventosInscripcionesCurso SET ProcesandoDesde = NULL WHERE InscripcionId = @p0";

        internal const string SQL_GUARDAR = @"
UPDATE dbo.EventosInscripcionesCurso
SET EmpresaPedido = ISNULL(@p1, EmpresaPedido), EmpresaFactura = @p2, Factura = @p3, EstadoLiquidacion = @p4, NumOrdenCobro = @p5,
    NumOrdenEfecto = @p6, Estado = @p7, Mensaje = @p8, Intentos = @p9, Usuario = @p10, FechaModificacion = GETDATE()
WHERE InscripcionId = @p0";

        internal const string SQL_CON_ERROR = @"
IF OBJECT_ID('dbo.EventosInscripcionesCurso') IS NOT NULL
    EXEC sp_executesql N'SELECT InscripcionId FROM dbo.EventosInscripcionesCurso WHERE Estado = ''Error'' AND Intentos < @max', N'@max int', @max = @p0
ELSE
    SELECT CAST(0 AS int) AS InscripcionId WHERE 1 = 0";

        // Facturadas sin cupón (corte 3): solo si ya existe la tabla de cupones.
        internal const string SQL_FACTURADAS_SIN_CUPON = @"
IF OBJECT_ID('dbo.EventosInscripcionesCurso') IS NOT NULL AND OBJECT_ID('dbo.Cupones') IS NOT NULL
    EXEC sp_executesql N'SELECT f.InscripcionId FROM dbo.EventosInscripcionesCurso f
        JOIN dbo.EventosInscripciones i ON i.Id = f.InscripcionId
        WHERE f.Estado = ''Facturada'' AND ISNULL(i.Modalidad, ''Reembolsable'') = ''Reembolsable''
          AND NOT EXISTS (SELECT 1 FROM dbo.Cupones c WHERE c.EventoInscripcionId = f.InscripcionId)'
ELSE
    SELECT CAST(0 AS int) AS InscripcionId WHERE 1 = 0";

        private readonly NVEntities db;

        public RepositorioFacturacionCursos(NVEntities db)
        {
            this.db = db;
        }

        public async Task<FacturacionCursoInscripcion> Leer(int inscripcionId) =>
            (await db.Database.SqlQuery<FacturacionCursoInscripcion>(SQL_LEER, new SqlParameter("@p0", inscripcionId))
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<Dictionary<int, FacturacionCursoInscripcion>> Leer(IEnumerable<int> inscripciones)
        {
            var resultado = new Dictionary<int, FacturacionCursoInscripcion>();
            List<int> ids = (inscripciones ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!ids.Any())
            {
                return resultado;
            }
            // Pocas inscripciones por evento o por cliente: la lista va en el texto (son enteros, sin riesgo de inyección).
            string lista = string.Join(",", ids);
            string sql = @"
IF OBJECT_ID('dbo.EventosInscripcionesCurso') IS NOT NULL
    EXEC sp_executesql N'SELECT " + COLUMNAS + " FROM dbo.EventosInscripcionesCurso WHERE InscripcionId IN (" + lista + @")'
ELSE
    SELECT CAST(0 AS int) AS InscripcionId, CAST(NULL AS varchar(3)) AS EmpresaPedido, CAST(NULL AS int) AS Pedido,
           CAST(NULL AS varchar(3)) AS EmpresaFactura, CAST(NULL AS varchar(10)) AS Factura, CAST(NULL AS varchar(20)) AS EstadoLiquidacion,
           CAST(NULL AS int) AS NumOrdenCobro, CAST(NULL AS int) AS NumOrdenEfecto, CAST(NULL AS varchar(20)) AS Estado,
           CAST(NULL AS nvarchar(1000)) AS Mensaje, 0 AS Intentos, CAST(NULL AS varchar(50)) AS Usuario, GETDATE() AS FechaCreacion,
           GETDATE() AS FechaModificacion
    WHERE 1 = 0";
            foreach (FacturacionCursoInscripcion fila in await db.Database.SqlQuery<FacturacionCursoInscripcion>(sql).ToListAsync().ConfigureAwait(false))
            {
                resultado[fila.InscripcionId] = fila;
            }
            return resultado;
        }

        public async Task<bool> Reclamar(int inscripcionId, string empresa, string usuario) =>
            (await db.Database.SqlQuery<int>(SQL_RECLAMAR,
                new SqlParameter("@p0", inscripcionId),
                new SqlParameter("@p1", empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO),
                new SqlParameter("@p2", Usuario(usuario))).ToListAsync().ConfigureAwait(false)).FirstOrDefault() > 0;

        public async Task Soltar(int inscripcionId) =>
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_SOLTAR, new SqlParameter("@p0", inscripcionId)).ConfigureAwait(false);

        public async Task Guardar(FacturacionCursoInscripcion fila) =>
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR,
                new SqlParameter("@p0", fila.InscripcionId),
                Nulable("@p1", fila.EmpresaPedido),
                Nulable("@p2", fila.EmpresaFactura),
                Nulable("@p3", fila.Factura),
                Nulable("@p4", fila.EstadoLiquidacion),
                Nulable("@p5", fila.NumOrdenCobro),
                Nulable("@p6", fila.NumOrdenEfecto),
                new SqlParameter("@p7", fila.Estado ?? EstadosFacturacionCurso.EN_CURSO),
                Nulable("@p8", fila.Mensaje),
                new SqlParameter("@p9", fila.Intentos),
                new SqlParameter("@p10", Usuario(fila.Usuario))).ConfigureAwait(false);

        public async Task<List<int>> ConError(int maxIntentos) =>
            await db.Database.SqlQuery<int>(SQL_CON_ERROR, new SqlParameter("@p0", maxIntentos)).ToListAsync().ConfigureAwait(false);

        public async Task<List<int>> FacturadasSinCupon() =>
            await db.Database.SqlQuery<int>(SQL_FACTURADAS_SIN_CUPON).ToListAsync().ConfigureAwait(false);

        internal const string SQL_PENDIENTES_EVENTO = @"
IF OBJECT_ID('dbo.EventosInscripcionesCurso') IS NOT NULL
    EXEC sp_executesql N'SELECT InscripcionId FROM dbo.EventosInscripcionesCurso WHERE Estado = ''PendienteEvento'''
ELSE
    SELECT CAST(0 AS int) AS InscripcionId WHERE 1 = 0";

        internal const string SQL_INSCRIPCION_DEL_PEDIDO = @"
IF OBJECT_ID('dbo.EventosInscripcionesCurso') IS NOT NULL
    EXEC sp_executesql N'SELECT TOP 1 InscripcionId FROM dbo.EventosInscripcionesCurso WHERE Pedido = @pedido ORDER BY InscripcionId DESC',
        N'@pedido int', @pedido = @p0
ELSE
    SELECT CAST(0 AS int) AS InscripcionId WHERE 1 = 0";

        public async Task<List<int>> PendientesDelEvento() =>
            await db.Database.SqlQuery<int>(SQL_PENDIENTES_EVENTO).ToListAsync().ConfigureAwait(false);

        public async Task<int?> InscripcionDelPedido(int pedido) =>
            (await db.Database.SqlQuery<int>(SQL_INSCRIPCION_DEL_PEDIDO, new SqlParameter("@p0", pedido)).ToListAsync().ConfigureAwait(false))
                .Select(i => (int?)i).FirstOrDefault();

        private static SqlParameter Nulable(string nombre, object valor) => new SqlParameter(nombre, valor ?? DBNull.Value);

        internal static string Usuario(string usuario)
        {
            string u = string.IsNullOrWhiteSpace(usuario) ? InterruptoresEventos.USUARIO_AUTOMATICO : usuario.Trim();
            return u.Length > 50 ? u.Substring(0, 50) : u;
        }
    }

    /// <summary>
    /// NestoAPI#612: el pedido del curso. Una línea con <see cref="Evento.ProductoCurso"/> (servicio ficticio, grupo CUR, IVA
    /// EX de su ficha: exento) a precio = señal, sin descuentos ni pronto pago, como los facturan hoy las tiendas: serie
    /// CV (SerieSegunLineas), contado, periodo normal (un cliente de fin de mes no se queda el curso sin facturar), almacén y
    /// delegación de la tienda del evento. Pedido y número en EventosInscripcionesCurso van en la misma transacción.
    /// </summary>
    public class CreadorPedidoCurso : ICreadorPedidoCurso
    {
        internal const string SQL_APUNTAR_PEDIDO = @"
UPDATE dbo.EventosInscripcionesCurso SET EmpresaPedido = @p1, Pedido = @p2, FechaModificacion = GETDATE()
WHERE InscripcionId = @p0 AND Pedido IS NULL";

        private readonly NVEntities db;
        private readonly Func<IServicioPedidosVenta> servicioPedidos;

        public CreadorPedidoCurso(NVEntities db, Func<IServicioPedidosVenta> servicioPedidos = null)
        {
            this.db = db;
            this.servicioPedidos = servicioPedidos ?? (() => new ServicioPedidosVenta());
        }

        public async Task<PedidoCursoCreado> Crear(EventoInscripcion inscripcion, Evento evento, string usuario)
        {
            string empresa = inscripcion.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string cliente = inscripcion.Cliente?.Trim();
            string contacto = inscripcion.Contacto?.Trim();
            Cliente ficha = (await db.Clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente).ToListAsync().ConfigureAwait(false))
                .FirstOrDefault(c => c.Contacto?.Trim() == contacto)
                ?? throw new InvalidOperationException($"No existe el cliente {cliente}/{contacto}.");
            if (string.IsNullOrWhiteSpace(ficha.IVA))
            {
                throw new InvalidOperationException($"El cliente {cliente}/{contacto} no tiene IVA en la ficha: no se le puede facturar el curso.");
            }

            IServicioPedidosVenta servicio = servicioPedidos();
            PlazoPago plazo = servicio.LeerPlazosPago(empresa, Constantes.PlazosPago.CONTADO)
                ?? throw new InvalidOperationException($"No existen los plazos de pago {Constantes.PlazosPago.CONTADO}.");
            DateTime hoy = DateTime.Today;
            // Carlos (22/10/26): SIEMPRE el almacén y la delegación del evento (también los online: el ponente emite desde una de
            // las dos tiendas). Sin tienda válida no se crea el pedido: nunca ALG.
            string almacen = ReglasFacturacionCurso.AlmacenDelCurso(evento);
            string usuarioPedido = Truncar(usuario, 30);
            // Carlos (22/10/26): las dos modalidades se facturan con el producto de Nesto sin NR (EVENTO00123, no EVENTO00123NR)
            string productoCurso = evento.ProductoCurso.Trim();
            if (ReglasProductoCurso.EsDelEvento(productoCurso, evento.Id))
            {
                string sinNr = ReglasProductoCurso.Referencia(evento.Id, ModalidadInscripcionEvento.Reembolsable);
                bool existe = sinNr == productoCurso
                    || await db.Productos.AnyAsync(p => p.Empresa == empresa && p.Número == sinNr).ConfigureAwait(false);
                productoCurso = ReglasProductoCurso.ParaFacturar(evento, _ => existe);
            }
            int numero = db.TomarSiguienteNumeroPedido();

            var cabecera = new CabPedidoVta
            {
                Empresa = empresa,
                Número = numero,
                Nº_Cliente = ficha.Nº_Cliente,
                Contacto = ficha.Contacto,
                Fecha = hoy,
                Forma_Pago = ReglasFacturacionCurso.FormaPagoPedido(inscripcion.FormaPago),
                PlazosPago = Constantes.PlazosPago.CONTADO,
                Primer_Vencimiento = hoy,
                IVA = ficha.IVA,
                Vendedor = ficha.Vendedor,
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL,
                Ruta = ficha.Ruta,
                Serie = Constantes.Series.SERIE_POR_DEFECTO,
                CCC = null,
                Origen = empresa,
                ContactoCobro = ficha.ContactoCobro,
                MantenerJunto = true,
                ServirJunto = true,
                ModoServicio = Constantes.Pedidos.ModosServicio.TODO_JUNTO,
                Comentarios = Truncar($"Curso «{evento.Titulo?.Trim()}» del {evento.Fecha:dd/MM/yyyy} (inscripción {inscripcion.Id}, NestoAPI#612)", 200),
                Usuario = usuarioPedido
            };

            var gestor = new GestorPedidosVenta(servicio);
            LinPedidoVta linea = gestor.CrearLineaVta(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = productoCurso,
                Cantidad = 1,
                PrecioUnitario = ReglasModalidadEvento.ImporteDe(inscripcion, evento), // NestoAPI#690: el de su modalidad
                AplicarDescuento = false,
                DescuentoLinea = 0,
                DescuentoProducto = 0,
                texto = evento.Titulo?.Trim(),
                estado = Constantes.EstadosLineaVenta.EN_CURSO,
                fechaEntrega = hoy,
                almacen = almacen,
                delegacion = almacen,
                formaVenta = InterruptoresEventos.FormaVentaCurso,
                usuario = usuarioPedido,
                vistoBueno = true
            }, numero, empresa, cabecera.IVA, plazo, ficha.Nº_Cliente, ficha.Contacto, ficha.Ruta, ficha.Vendedor, null);
            // El curso vale lo que la señal: ni descuento de cliente ni pronto pago (la factura tiene que cuadrar con el cobro).
            linea.DescuentoCliente = 0;
            linea.DescuentoPP = 0;
            linea.Descuento = 0;
            linea.Aplicar_Dto = false;
            linea.Fecha_Entrega = hoy;
            gestor.CalcularImportesLinea(linea, cabecera.IVA);
            cabecera.Serie = SerieSegunLineas.Resolver(cabecera.Serie, empresa, new[] { linea });

            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int apuntado = await db.Database.ExecuteSqlCommandAsync(SQL_APUNTAR_PEDIDO,
                        new SqlParameter("@p0", inscripcion.Id), new SqlParameter("@p1", empresa), new SqlParameter("@p2", numero)).ConfigureAwait(false);
                    if (apuntado == 0)
                    {
                        throw new InvalidOperationException($"La inscripción {inscripcion.Id} ya tiene pedido del curso (lo ha creado otro proceso).");
                    }
                    _ = db.CabPedidoVtas.Add(cabecera);
                    _ = db.LinPedidoVtas.Add(linea);
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                }
                catch
                {
                    transaccion.Rollback();
                    // Que el siguiente SaveChanges del mismo contexto no vuelva a intentar meterlos
                    foreach (DbEntityEntry entrada in db.ChangeTracker.Entries().Where(e => e.Entity == cabecera || e.Entity == linea).ToList())
                    {
                        entrada.State = EntityState.Detached;
                    }
                    _ = Controllers.PedidosVentaController.DevolverNumeroPedidoNoGuardado(db, numero, true);
                    throw;
                }
            }
            return new PedidoCursoCreado { Empresa = empresa, Pedido = numero };
        }

        private static string Truncar(string texto, int max) =>
            string.IsNullOrEmpty(texto) ? texto : texto.Length > max ? texto.Substring(0, max) : texto;
    }

    /// <summary>
    /// NestoAPI#612: factura al momento, sin esperar a rutas: albarán si no lo tiene (como los pedidos FBA de Amazon, Nesto#434)
    /// y ServicioFacturas.CrearFactura (único sitio de prdCrearFacturaVta), con su propio contexto para que pueda traspasar
    /// a la empresa espejo si toca. Si el pedido ya está facturado (lo facturó alguien a mano), devuelve esa factura.
    /// </summary>
    public class FacturadorPedidoCurso : IFacturadorPedidoCurso
    {
        private readonly Func<NVEntities> nuevoContexto;
        private readonly Func<IServicioAlbaranesVenta> albaranes;
        private readonly Func<IServicioFacturas> facturas;

        public FacturadorPedidoCurso(Func<NVEntities> nuevoContexto = null, Func<IServicioAlbaranesVenta> albaranes = null,
            Func<IServicioFacturas> facturas = null)
        {
            this.nuevoContexto = nuevoContexto ?? (() => new NVEntities());
            this.albaranes = albaranes ?? (() => new ServicioAlbaranesVenta());
            this.facturas = facturas ?? (() => new ServicioFacturas());
        }

        public async Task<FacturaCursoCreada> Facturar(string empresa, int pedido, string usuario)
        {
            using (NVEntities db = nuevoContexto())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                // Numeración global: tras un traspaso el pedido vive en la espejo con el mismo número
                var lineas = await db.LinPedidoVtas.Where(l => l.Número == pedido)
                    .Select(l => new { l.Empresa, l.Estado, l.Nº_Factura }).ToListAsync().ConfigureAwait(false);
                var facturada = lineas.FirstOrDefault(l => l.Estado == Constantes.EstadosLineaVenta.FACTURA && l.Nº_Factura != null);
                if (facturada != null)
                {
                    return new FacturaCursoCreada { EmpresaPedido = facturada.Empresa?.Trim(), EmpresaFactura = facturada.Empresa?.Trim(), Factura = facturada.Nº_Factura.Trim() };
                }
                if (!lineas.Any())
                {
                    throw new InvalidOperationException($"No existe el pedido {pedido} del curso (¿lo han borrado?).");
                }
                string empresaPedido = lineas.First().Empresa?.Trim() ?? empresa;
                if (!lineas.Any(l => l.Estado == Constantes.EstadosLineaVenta.ALBARAN))
                {
                    _ = await albaranes().CrearAlbaran(empresaPedido, pedido, usuario, DateTime.Today).ConfigureAwait(false);
                }
                CrearFacturaResponseDTO creada = await facturas().CrearFactura(empresaPedido, pedido, usuario, usuario).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(creada?.NumeroFactura) || creada.NumeroFactura.Trim() == Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES)
                {
                    throw new InvalidOperationException($"El pedido {pedido} del curso no se ha facturado.");
                }
                string empresaFactura = creada.Empresa?.Trim() ?? empresaPedido;
                return new FacturaCursoCreada { EmpresaPedido = empresaFactura, EmpresaFactura = empresaFactura, Factura = creada.NumeroFactura.Trim() };
            }
        }
    }

    /// <summary>
    /// NestoAPI#612: liquida el cobro de la señal contra el efecto de la factura del curso, «lo mismo que hace hoy el botón
    /// manual» (ServicioLiquidarEfectos, único sitio de prdLiquidar). El cobro es el apunte de la señal de #591 (transferencia)
    /// o el cobro a cuenta del enlace de la inscripción (Documento «INS{id}», que apunta ServicioPagos). En efectivo, en el
    /// datáfono o con una transferencia marcada a mano no hay cobro en el extracto: la factura queda pendiente de cobro y se
    /// cobra como cualquier otra (la caja de la tienda, Administración).
    /// </summary>
    public class LiquidadorSenalCurso : ILiquidadorSenalCurso
    {
        private readonly NVEntities db;
        private readonly IServicioLiquidarEfectos liquidar;

        public LiquidadorSenalCurso(NVEntities db, IServicioLiquidarEfectos liquidar = null)
        {
            this.db = db;
            this.liquidar = liquidar ?? new ServicioLiquidarEfectos(db);
        }

        public async Task<LiquidacionSenalCurso> Liquidar(EventoInscripcion inscripcion, string empresaFactura, string factura, string usuario)
        {
            string empresa = inscripcion.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string cliente = inscripcion.Cliente?.Trim();
            int? cobro = null;
            string deDonde;
            if (inscripcion.EventoSenalId.HasValue)
            {
                int senalId = inscripcion.EventoSenalId.Value;
                EventoSenal senal = await db.EventosSenales.SingleOrDefaultAsync(s => s.Id == senalId).ConfigureAwait(false);
                deDonde = $"la señal {senalId}";
                if (senal != null)
                {
                    int orden = senal.NumOrdenExtracto;
                    string empresaSenal = senal.Empresa?.Trim() ?? empresa;
                    cobro = await db.ExtractosCliente.Where(e => e.Empresa == empresaSenal && e.Nº_Orden == orden && e.ImportePdte < 0)
                        .Select(e => (int?)e.Nº_Orden).FirstOrDefaultAsync().ConfigureAwait(false);
                    deDonde = $"la señal (apunte {orden})";
                }
            }
            else if (inscripcion.PagoTPVId.HasValue && ReglasInscripcionEvento.FormaPago(inscripcion.FormaPago) == FormaPagoInscripcionEvento.Tarjeta)
            {
                string documento = ServicioInscripcionesEventos.DocumentoPago(inscripcion.Id);
                deDonde = $"el cobro del enlace ({documento})";
                cobro = await db.ExtractosCliente
                    .Where(e => e.Empresa == empresa && e.Número == cliente && e.Nº_Documento == documento
                        && e.TipoApunte == Constantes.TiposExtractoCliente.PAGO && e.ImportePdte < 0)
                    .OrderByDescending(e => e.Nº_Orden).Select(e => (int?)e.Nº_Orden).FirstOrDefaultAsync().ConfigureAwait(false);
            }
            else
            {
                return new LiquidacionSenalCurso
                {
                    Estado = EstadosLiquidacionCurso.SIN_COBRO,
                    Mensaje = $"Pagado {DescribirForma(inscripcion)}: no hay cobro en el extracto que liquidar. La factura {factura} " +
                        "queda pendiente de cobro y se cobra como cualquier otra."
                };
            }

            if (cobro == null)
            {
                return new LiquidacionSenalCurso
                {
                    Estado = EstadosLiquidacionCurso.SIN_COBRO,
                    AvisarAdministracion = true,
                    Mensaje = $"No se encuentra en el extracto {deDonde} con importe pendiente a favor del cliente: liquide a mano la factura {factura}."
                };
            }
            if (!string.Equals(empresaFactura?.Trim(), empresa, StringComparison.OrdinalIgnoreCase))
            {
                return new LiquidacionSenalCurso
                {
                    Estado = EstadosLiquidacionCurso.SIN_COBRO,
                    NumOrdenCobro = cobro,
                    AvisarAdministracion = true,
                    Mensaje = $"La factura {factura} es de la empresa {empresaFactura?.Trim()} y {deDonde} de la {empresa}: no se pueden liquidar " +
                        "entre sí. Hágalo a mano."
                };
            }
            string empresaEfecto = empresaFactura.Trim();
            string numeroFactura = factura?.Trim();
            int? efecto = await db.ExtractosCliente
                .Where(e => e.Empresa == empresaEfecto && e.Número == cliente && e.Nº_Documento == numeroFactura
                    && e.TipoApunte == Constantes.TiposExtractoCliente.CARTERA && e.ImportePdte > 0)
                .OrderBy(e => e.Nº_Orden).Select(e => (int?)e.Nº_Orden).FirstOrDefaultAsync().ConfigureAwait(false);
            if (efecto == null)
            {
                return new LiquidacionSenalCurso
                {
                    Estado = EstadosLiquidacionCurso.SIN_COBRO,
                    NumOrdenCobro = cobro,
                    AvisarAdministracion = true,
                    Mensaje = $"La factura {numeroFactura} no tiene efecto pendiente en el extracto: revise a mano {deDonde}."
                };
            }

            ResultadoLiquidacionEfectos resultado = await liquidar.Liquidar(empresa, cobro.Value, efecto.Value, Truncar(usuario, 30)).ConfigureAwait(false);
            if (!resultado.Exito)
            {
                // Vuelve a intentarlo desde aquí (la factura ya está apuntada)
                throw new InvalidOperationException($"No se ha podido liquidar {deDonde} contra la factura {numeroFactura}: " +
                    string.Join(" ", resultado.Errores));
            }
            return new LiquidacionSenalCurso { Estado = EstadosLiquidacionCurso.LIQUIDADA, NumOrdenCobro = cobro, NumOrdenEfecto = efecto };
        }

        private static string DescribirForma(EventoInscripcion inscripcion)
        {
            // NestoAPI#614: la compra del curso en la tienda se cobra como los demás pedidos de la tienda
            if (inscripcion.Origen?.Trim() == OrigenInscripcionEvento.PrestaShop.ToString())
            {
                return "en la tienda online (PrestaShop)";
            }
            switch (ReglasInscripcionEvento.FormaPago(inscripcion.FormaPago))
            {
                case FormaPagoInscripcionEvento.Efectivo: return "en efectivo";
                case FormaPagoInscripcionEvento.Tarjeta: return "con tarjeta (datáfono)";
                default: return "por transferencia marcada a mano";
            }
        }

        private static string Truncar(string texto, int max) =>
            string.IsNullOrEmpty(texto) ? texto : texto.Length > max ? texto.Substring(0, max) : texto;
    }

    /// <summary>NestoAPI#612: el correo a Administración (lo que no se ha podido automatizar o ha fallado). Nunca lanza.</summary>
    public class AvisadorFacturacionCurso : IAvisadorFacturacionCurso
    {
        private readonly IServicioCorreoElectronico correo;

        public AvisadorFacturacionCurso(IServicioCorreoElectronico correo = null)
        {
            this.correo = correo ?? new ServicioCorreoElectronico();
        }

        public Task Avisar(string asunto, string cuerpoHtml)
        {
            try
            {
                using (var mail = new MailMessage
                {
                    From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                    Subject = asunto,
                    Body = "<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>" +
                        cuerpoHtml + "</body></html>",
                    IsBodyHtml = true,
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8
                })
                {
                    mail.To.Add(Constantes.Correos.CORREO_ADMON);
                    if (!correo.EnviarCorreoSMTP(mail))
                    {
                        ElmahHelper.Log(new Exception($"[Cursos #612] No se ha podido mandar a Administración el aviso «{asunto}»."));
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cursos #612] No se ha podido mandar a Administración el aviso «{asunto}»: {ex.Message}", ex));
            }
            return Task.CompletedTask;
        }
    }
}
