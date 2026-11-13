using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos.PagoAPlazos
{
    /// <summary>
    /// NestoAPI#864: lo que el núcleo lee y escribe de dbo.SolicitudesPagoAPlazos. Detrás de una interfaz para probar el
    /// núcleo sin base de datos.
    /// </summary>
    public interface IRepositorioPagoAPlazos
    {
        /// <summary>¿Está la tabla (Scripts/Issue864_PagoAPlazos.sql lanzado)?</summary>
        bool TablaDisponible();

        /// <summary>Da de alta la solicitud (Pendiente) y devuelve su Id.</summary>
        Task<int> Crear(SolicitudPagoAPlazos solicitud);

        /// <summary>Guarda nuestra referencia y la del proveedor (las que no sean null).</summary>
        Task GuardarReferencias(int id, string referencia, string referenciaExterna);

        Task<SolicitudPagoAPlazos> Leer(int id);
        Task<SolicitudPagoAPlazos> LeerPorReferencia(string proveedor, string referencia);
        Task<List<SolicitudPagoAPlazos>> LeerDelPedido(string empresa, int pedido);

        /// <summary>Las pendientes y las aprobadas (lo que repasa el job).</summary>
        Task<List<SolicitudPagoAPlazos>> LeerVivas();

        /// <summary>
        /// Cambia el estado SOLO si está en uno de <paramref name="desde"/> (UPDATE condicional: la notificación del proveedor,
        /// el job y una cancelación pueden llegar a la vez). A Aprobado: apunta la aprobación y el límite de captura; a
        /// Cancelado, Caducado o Rechazado: apunta el cierre y el motivo. Devuelve si lo ha cambiado.
        /// </summary>
        Task<bool> CambiarEstado(int id, IEnumerable<string> desde, string a, DateTime ahora, string motivo = null, DateTime? fechaLimiteCaptura = null);

        /// <summary>Aprobado → Capturado, con la fecha y el importe. Devuelve si lo ha cambiado.</summary>
        Task<bool> GuardarCaptura(int id, DateTime fecha, decimal importe, string motivo = null);

        Task MarcarAvisoPlazo(int id, DateTime fecha);
        Task GuardarPrepago(int id, int prepagoId);
    }

    /// <summary>El prepago que ha quedado apuntado en el pedido.</summary>
    public class PrepagoPagoAPlazos
    {
        public int Id { get; set; }
        /// <summary>False: ya estaba (notificación repetida).</summary>
        public bool Creado { get; set; }
    }

    /// <summary>NestoAPI#864: lo que el núcleo necesita del pedido y de sus prepagos.</summary>
    public interface IPedidosPagoAPlazos
    {
        /// <summary>Null si el pedido no existe.</summary>
        Task<PedidoPagoAPlazos> Leer(string empresa, int pedido);

        /// <summary>
        /// Apunta el Prepago al pedido con la fecha real del cobro (Prepagos.FechaCobro, #860). Idempotente por el concepto.
        /// Null si el pedido no existe.
        /// </summary>
        Task<PrepagoPagoAPlazos> AnadirPrepago(string empresa, int pedido, decimal importe, string cuenta, string concepto,
            string usuario, DateTime fechaCobro);

        /// <summary>Quita el Prepago si todavía no se ha facturado. Devuelve si lo ha quitado.</summary>
        Task<bool> QuitarPrepago(int prepagoId);

        /// <summary>Deja el Prepago en ese importe si todavía no se ha facturado. Devuelve si lo ha cambiado.</summary>
        Task<bool> AjustarPrepago(int prepagoId, decimal importe);
    }

    /// <summary>
    /// NestoAPI#864: la tabla dbo.SolicitudesPagoAPlazos, FUERA del EDMX a propósito (regla «EDMX a mano → validar
    /// metadatos»): se lee y escribe con SQL parametrizado y antes se mira si existe (<see cref="ColumnasOpcionales"/>).
    /// Publicar la API antes de lanzar el script no rompe nada: los endpoints nuevos dan 503 y lo demás sigue igual.
    /// </summary>
    public class RepositorioPagoAPlazos : IRepositorioPagoAPlazos
    {
        internal const string TABLA = "SolicitudesPagoAPlazos";

        internal const string COLUMNAS = "Id, Empresa, Pedido, Cliente, Contacto, Proveedor, Referencia, ReferenciaExterna, Importe, " +
            "Canal, Estado, Pruebas, Usuario, Correo, FechaCreacion, FechaAprobacion, FechaLimiteCaptura, FechaCaptura, " +
            "ImporteCapturado, FechaAvisoPlazo, FechaCierre, Motivo, PrepagoId";

        internal const string SQL_LEER = "SELECT " + COLUMNAS + " FROM dbo.SolicitudesPagoAPlazos WHERE Id = @id";

        internal const string SQL_LEER_POR_REFERENCIA = "SELECT " + COLUMNAS +
            " FROM dbo.SolicitudesPagoAPlazos WHERE Proveedor = @proveedor AND Referencia = @referencia";

        internal const string SQL_LEER_DEL_PEDIDO = "SELECT " + COLUMNAS +
            " FROM dbo.SolicitudesPagoAPlazos WHERE Empresa = @empresa AND Pedido = @pedido ORDER BY Id";

        internal const string SQL_LEER_VIVAS = "SELECT " + COLUMNAS +
            " FROM dbo.SolicitudesPagoAPlazos WHERE Estado IN ('Pendiente', 'Aprobado') ORDER BY Id";

        internal const string SQL_CREAR =
            "INSERT INTO dbo.SolicitudesPagoAPlazos (Empresa, Pedido, Cliente, Contacto, Proveedor, Importe, Canal, Estado, Pruebas, " +
            "Usuario, Correo, FechaCreacion) " +
            "VALUES (@empresa, @pedido, @cliente, @contacto, @proveedor, @importe, @canal, @estado, @pruebas, @usuario, @correo, @fecha); " +
            "SELECT CAST(SCOPE_IDENTITY() AS int)";

        internal const string SQL_REFERENCIAS =
            "UPDATE dbo.SolicitudesPagoAPlazos SET Referencia = COALESCE(@referencia, Referencia), " +
            "ReferenciaExterna = COALESCE(@externa, ReferenciaExterna) WHERE Id = @id";

        internal const string SQL_CAPTURA =
            "UPDATE dbo.SolicitudesPagoAPlazos SET Estado = 'Capturado', FechaCaptura = @fecha, ImporteCapturado = @importe, " +
            "Motivo = COALESCE(@motivo, Motivo) WHERE Id = @id AND Estado = 'Aprobado'";

        internal const string SQL_AVISO = "UPDATE dbo.SolicitudesPagoAPlazos SET FechaAvisoPlazo = @fecha WHERE Id = @id";

        internal const string SQL_PREPAGO = "UPDATE dbo.SolicitudesPagoAPlazos SET PrepagoId = @prepago WHERE Id = @id";

        private static NVEntities Contexto()
        {
            NVEntities db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            return db;
        }

        private static object ONulo(object valor) => valor ?? DBNull.Value;

        public bool TablaDisponible()
        {
            using (NVEntities db = Contexto())
            {
                return ColumnasOpcionales.ExisteTabla(db, TABLA);
            }
        }

        public async Task<int> Crear(SolicitudPagoAPlazos solicitud)
        {
            using (NVEntities db = Contexto())
            {
                return await db.Database.SqlQuery<int>(SQL_CREAR,
                    new SqlParameter("@empresa", solicitud.Empresa),
                    new SqlParameter("@pedido", solicitud.Pedido),
                    new SqlParameter("@cliente", ONulo(solicitud.Cliente)),
                    new SqlParameter("@contacto", ONulo(solicitud.Contacto)),
                    new SqlParameter("@proveedor", solicitud.Proveedor),
                    new SqlParameter("@importe", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = solicitud.Importe },
                    new SqlParameter("@canal", solicitud.Canal),
                    new SqlParameter("@estado", solicitud.Estado),
                    new SqlParameter("@pruebas", solicitud.Pruebas),
                    new SqlParameter("@usuario", ONulo(solicitud.Usuario)),
                    new SqlParameter("@correo", ONulo(solicitud.Correo)),
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = solicitud.FechaCreacion })
                    .FirstAsync().ConfigureAwait(false);
            }
        }

        public async Task GuardarReferencias(int id, string referencia, string referenciaExterna)
        {
            using (NVEntities db = Contexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_REFERENCIAS,
                    new SqlParameter("@referencia", SqlDbType.VarChar, 40) { Value = ONulo(referencia) },
                    new SqlParameter("@externa", SqlDbType.NVarChar, 100) { Value = ONulo(referenciaExterna) },
                    new SqlParameter("@id", id)).ConfigureAwait(false);
            }
        }

        public async Task<SolicitudPagoAPlazos> Leer(int id)
        {
            using (NVEntities db = Contexto())
            {
                return await db.Database.SqlQuery<SolicitudPagoAPlazos>(SQL_LEER, new SqlParameter("@id", id))
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<SolicitudPagoAPlazos> LeerPorReferencia(string proveedor, string referencia)
        {
            using (NVEntities db = Contexto())
            {
                return await db.Database.SqlQuery<SolicitudPagoAPlazos>(SQL_LEER_POR_REFERENCIA,
                    new SqlParameter("@proveedor", proveedor ?? string.Empty),
                    new SqlParameter("@referencia", referencia ?? string.Empty))
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<SolicitudPagoAPlazos>> LeerDelPedido(string empresa, int pedido)
        {
            using (NVEntities db = Contexto())
            {
                return await db.Database.SqlQuery<SolicitudPagoAPlazos>(SQL_LEER_DEL_PEDIDO,
                    new SqlParameter("@empresa", empresa ?? string.Empty),
                    new SqlParameter("@pedido", pedido))
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<List<SolicitudPagoAPlazos>> LeerVivas()
        {
            using (NVEntities db = Contexto())
            {
                return await db.Database.SqlQuery<SolicitudPagoAPlazos>(SQL_LEER_VIVAS).ToListAsync().ConfigureAwait(false);
            }
        }

        /// <summary>El UPDATE condicional del cambio de estado. Internal para los tests.</summary>
        internal static string SqlCambiarEstado(int estadosDePartida)
        {
            string enLista = string.Join(", ", Enumerable.Range(0, estadosDePartida).Select(i => "@desde" + i));
            return "UPDATE dbo.SolicitudesPagoAPlazos SET Estado = @a, " +
                "FechaAprobacion = CASE WHEN @a = 'Aprobado' THEN @ahora ELSE FechaAprobacion END, " +
                "FechaLimiteCaptura = COALESCE(@limite, FechaLimiteCaptura), " +
                "FechaCierre = CASE WHEN @a IN ('Cancelado', 'Caducado', 'Rechazado') THEN @ahora ELSE FechaCierre END, " +
                "Motivo = COALESCE(@motivo, Motivo) " +
                $"WHERE Id = @id AND Estado IN ({enLista})";
        }

        public async Task<bool> CambiarEstado(int id, IEnumerable<string> desde, string a, DateTime ahora, string motivo = null,
            DateTime? fechaLimiteCaptura = null)
        {
            List<string> estados = (desde ?? Enumerable.Empty<string>()).ToList();
            if (estados.Count == 0)
            {
                return false;
            }
            List<object> parametros = new List<object>
            {
                new SqlParameter("@id", id),
                new SqlParameter("@a", SqlDbType.VarChar, 15) { Value = a },
                new SqlParameter("@ahora", SqlDbType.DateTime) { Value = ahora },
                new SqlParameter("@limite", SqlDbType.DateTime) { Value = ONulo(fechaLimiteCaptura) },
                new SqlParameter("@motivo", SqlDbType.NVarChar, 300) { Value = ONulo(Recortar(motivo, 300)) }
            };
            parametros.AddRange(estados.Select((e, i) => (object)new SqlParameter("@desde" + i, SqlDbType.VarChar, 15) { Value = e }));
            using (NVEntities db = Contexto())
            {
                return await db.Database.ExecuteSqlCommandAsync(SqlCambiarEstado(estados.Count), parametros.ToArray()).ConfigureAwait(false) > 0;
            }
        }

        public async Task<bool> GuardarCaptura(int id, DateTime fecha, decimal importe, string motivo = null)
        {
            using (NVEntities db = Contexto())
            {
                return await db.Database.ExecuteSqlCommandAsync(SQL_CAPTURA,
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = fecha },
                    new SqlParameter("@importe", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = importe },
                    new SqlParameter("@motivo", SqlDbType.NVarChar, 300) { Value = ONulo(Recortar(motivo, 300)) },
                    new SqlParameter("@id", id)).ConfigureAwait(false) > 0;
            }
        }

        public async Task MarcarAvisoPlazo(int id, DateTime fecha)
        {
            using (NVEntities db = Contexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_AVISO,
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = fecha },
                    new SqlParameter("@id", id)).ConfigureAwait(false);
            }
        }

        public async Task GuardarPrepago(int id, int prepagoId)
        {
            using (NVEntities db = Contexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_PREPAGO,
                    new SqlParameter("@prepago", prepagoId),
                    new SqlParameter("@id", id)).ConfigureAwait(false);
            }
        }

        internal static string Recortar(string texto, int largo)
            => texto == null ? null : texto.Length > largo ? texto.Substring(0, largo) : texto;
    }

    /// <summary>NestoAPI#864: el pedido y sus prepagos, de la base de datos. No se testa porque todo es lectura y escritura de BD.</summary>
    public class PedidosPagoAPlazos : IPedidosPagoAPlazos
    {
        private static NVEntities Contexto()
        {
            NVEntities db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            return db;
        }

        public async Task<PedidoPagoAPlazos> Leer(string empresa, int pedido)
        {
            using (NVEntities db = Contexto())
            {
                CabPedidoVta cab = await db.CabPedidoVtas.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Empresa == empresa && c.Número == pedido).ConfigureAwait(false);
                if (cab == null)
                {
                    return null;
                }
                string cliente = cab.Nº_Cliente?.Trim();
                string contacto = cab.Contacto?.Trim();

                var lineas = await db.LinPedidoVtas.AsNoTracking()
                    .Where(l => l.Empresa == empresa && l.Número == pedido)
                    .Select(l => new
                    {
                        l.TipoLinea, l.Producto, l.Texto, l.Cantidad, l.Base_Imponible, l.PorcentajeIVA, l.PorcentajeRE, l.Total,
                        l.Estado, l.Fecha_Entrega
                    })
                    .ToListAsync().ConfigureAwait(false);

                decimal prepagos = await db.Prepagos
                    .Where(p => p.Empresa == empresa && p.Pedido == pedido)
                    .Select(p => p.Importe).DefaultIfEmpty(0).SumAsync().ConfigureAwait(false);

                decimal enlaces = 0;
                try
                {
                    enlaces = EnlacesPagoPedido.ImporteAutorizado(LectorEnlacesPagoPedido.Leer(db, empresa, pedido));
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Pago a plazos #864] No se han podido leer los enlaces de pago del pedido {pedido}: {ex.Message}", ex));
                }

                List<Cliente> fichas = await db.Clientes.AsNoTracking()
                    .Where(c => c.Empresa == empresa && c.Nº_Cliente == cab.Nº_Cliente)
                    .ToListAsync().ConfigureAwait(false);
                Cliente entrega = fichas.FirstOrDefault(c => c.Contacto?.Trim() == contacto);
                Cliente principal = fichas.FirstOrDefault(c => c.ClientePrincipal) ?? entrega;

                List<string> correos = await db.PersonasContactoClientes
                    .Where(p => p.Empresa == empresa && p.NºCliente == cab.Nº_Cliente && p.CorreoElectrónico != null && p.CorreoElectrónico != "")
                    .OrderBy(p => p.Contacto == cab.Contacto ? 0 : 1)
                    .Select(p => p.CorreoElectrónico)
                    .ToListAsync().ConfigureAwait(false);

                string aplicacion = Constantes.Aplicaciones.NESTO_TIENDAS;
                bool tieneApp = !string.IsNullOrEmpty(cliente) && await db.DispositivosNotificaciones
                    .AnyAsync(d => (d.Empresa == null || d.Empresa == empresa) && d.Cliente == cliente && d.Aplicacion == aplicacion && d.Activo)
                    .ConfigureAwait(false);

                return new PedidoPagoAPlazos
                {
                    Empresa = empresa?.Trim(),
                    Pedido = pedido,
                    Cliente = cliente,
                    Contacto = contacto,
                    NombreCliente = (entrega ?? principal)?.Nombre?.Trim(),
                    EsNotaEntrega = cab.NotaEntrega,
                    ContraReembolso = GestorPortes.EsContraReembolso(cab.Forma_Pago?.Trim(), cab.PlazosPago, cab.CCC,
                        cab.Periodo_Facturacion?.Trim(), cab.NotaEntrega),
                    RecogidaEnTienda = TiendasRecogida.EsRutaDeRecogida(cab.Ruta?.Trim()),
                    Lineas = lineas.Select(l => new LineaPagoAPlazos
                    {
                        EsProducto = l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO,
                        Producto = l.Producto?.Trim(),
                        Texto = l.Texto?.Trim(),
                        Cantidad = l.Cantidad ?? 0,
                        BaseImponible = l.Base_Imponible,
                        Impuestos = (l.PorcentajeIVA / 100M) + l.PorcentajeRE,
                        Total = l.Total,
                        Estado = l.Estado,
                        FechaEntrega = l.Fecha_Entrega
                    }).ToList(),
                    Prepagos = prepagos,
                    CobradoPorEnlaces = enlaces,
                    Correo = correos.Select(c => c?.Trim()).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && c.Contains("@")),
                    Facturacion = Direccion(principal),
                    Envio = Direccion(entrega ?? principal),
                    ClienteTieneApp = tieneApp
                };
            }
        }

        private static DireccionPagoAPlazos Direccion(Cliente ficha) => ficha == null ? null : new DireccionPagoAPlazos
        {
            Nombre = ficha.Nombre?.Trim(),
            Direccion = ficha.Dirección?.Trim(),
            CodigoPostal = ficha.CodPostal?.Trim(),
            Poblacion = ficha.Población?.Trim(),
            Provincia = ficha.Provincia?.Trim(),
            Pais = ficha.Pais?.Trim(),
            Telefono = ficha.Teléfono?.Trim()
        };

        public async Task<PrepagoPagoAPlazos> AnadirPrepago(string empresa, int pedido, decimal importe, string cuenta, string concepto,
            string usuario, DateTime fechaCobro)
        {
            using (NVEntities db = Contexto())
            {
                bool existe = await db.CabPedidoVtas.AnyAsync(c => c.Empresa == empresa && c.Número == pedido).ConfigureAwait(false);
                if (!existe)
                {
                    return null;
                }
                Prepago prepago = await db.Prepagos
                    .FirstOrDefaultAsync(p => p.Empresa == empresa && p.Pedido == pedido && p.ConceptoAdicional == concepto)
                    .ConfigureAwait(false);
                bool creado = prepago == null;
                if (creado)
                {
                    prepago = new Prepago
                    {
                        Empresa = empresa,
                        Pedido = pedido,
                        Importe = importe,
                        CuentaContable = cuenta,
                        ConceptoAdicional = concepto,
                        Usuario = UsuarioAuditoriaHelper.ParaAuditoria(usuario)
                    };
                    _ = db.Prepagos.Add(prepago);
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                }
                // NestoAPI#860: la fecha real del cobro (la columna puede no estar: entonces no se escribe y se factura como siempre)
                new RepositorioFechaCobroPrepagos(db).Guardar(new Dictionary<int, DateTime> { { prepago.Id, fechaCobro.Date } });
                return new PrepagoPagoAPlazos { Id = prepago.Id, Creado = creado };
            }
        }

        public async Task<bool> QuitarPrepago(int prepagoId)
        {
            using (NVEntities db = Contexto())
            {
                Prepago prepago = await db.Prepagos.FirstOrDefaultAsync(p => p.Id == prepagoId).ConfigureAwait(false);
                if (prepago == null || !string.IsNullOrWhiteSpace(prepago.Factura))
                {
                    return false;
                }
                _ = db.Prepagos.Remove(prepago);
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }

        public async Task<bool> AjustarPrepago(int prepagoId, decimal importe)
        {
            using (NVEntities db = Contexto())
            {
                Prepago prepago = await db.Prepagos.FirstOrDefaultAsync(p => p.Id == prepagoId).ConfigureAwait(false);
                if (prepago == null || !string.IsNullOrWhiteSpace(prepago.Factura) || prepago.Importe == importe)
                {
                    return false;
                }
                prepago.Importe = importe;
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }
    }
}
