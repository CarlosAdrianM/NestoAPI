using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.CambioFiscal
{
    /// <summary>NestoAPI#876: lo que se lee de los dos clientes para decidir la copia.</summary>
    public class DatosCambioFiscal
    {
        public FotoClienteCambioFiscal Anterior { get; set; }
        public FotoClienteCambioFiscal Nuevo { get; set; }
        /// <summary>Los clientes anteriores con los que ya consta enlazado el nuevo (dbo.ClientesAnteriores).</summary>
        public List<string> AnterioresDelNuevo { get; set; } = new List<string>();
        /// <summary>El origen del nuevo (dbo.ClientesOrigen), o null.</summary>
        public string OrigenNuevo { get; set; }
    }

    /// <summary>NestoAPI#876: lo que se decide dentro de la transacción (con lo leído bajo el bloqueo).</summary>
    public class DecisionCopiaCambioFiscal
    {
        public PlanCopiaCambioFiscal Plan { get; set; }
        public bool CrearEnlace { get; set; }
        /// <summary>False = no se escribe nada (no vale, o no hay nada que copiar y el enlace ya está).</summary>
        public bool Escribir { get; set; }
        /// <summary>Si son «el mismo cliente con otra forma fiscal» (con lo leído bajo el bloqueo).</summary>
        public DecisionEnlaceCambioFiscal Enlace { get; set; }
        /// <summary>Lo que va a Modificaciones (Tabla ClienteCambioFiscal, Nuevo).</summary>
        public object Rastro { get; set; }
    }

    public interface IRepositorioCambioFiscal
    {
        /// <summary>Si está dbo.ClientesAnteriores (Scripts/Issue876_ClientesAnteriores.sql). Sin ella, 503.</summary>
        Task<bool> TablaExiste();

        /// <summary>Solo lectura (previa y ensayo).</summary>
        Task<DatosCambioFiscal> Leer(string empresa, string anterior, string nuevo);

        /// <summary>
        /// En UNA transacción con <c>sp_getapplock</c> «ClienteCambioFiscal:{empresa}:{nuevo}»: lee, llama a <paramref name="decidir"/>
        /// con lo leído y, si dice Escribir, crea el enlace, aplica el plan y deja la fila en Modificaciones.
        /// </summary>
        Task<DecisionCopiaCambioFiscal> Copiar(string empresa, string anterior, string nuevo, Func<DatosCambioFiscal, DecisionCopiaCambioFiscal> decidir, string usuario);

        /// <summary>El enlace del alta (idempotente): el nuevo es el anterior con otra forma fiscal.</summary>
        Task CrearEnlace(string empresa, string nuevo, string anterior, string usuario);
    }

    /// <summary>NestoAPI#876: ADO sobre NestoConnection. Clientes, CondPagoClientes, DescuentosProducto, OfertasPermitidas y
    /// VendedoresClienteGrupoProducto ya los escribía la API; dbo.ClientesAnteriores es nueva (fuera del EDMX).</summary>
    public class RepositorioCambioFiscalSql : IRepositorioCambioFiscal
    {
        private readonly string cadenaConexion;

        public RepositorioCambioFiscalSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion;
        }

        internal const string SQL_TABLA = "SELECT CASE WHEN OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NULL THEN 0 ELSE 1 END";

        internal const string SQL_FICHAS = @"
SELECT RTRIM(Contacto) AS Contacto, ClientePrincipal, RTRIM(Nombre) AS Nombre, Estado, RTRIM(Vendedor) AS Vendedor, RTRIM(IVA) AS IVA,
       Comentarios, RTRIM(DiasEnServir) AS DiasEnServir, RTRIM(Ruta) AS Ruta, RTRIM(PeriodoFacturación) AS PeriodoFacturacion,
       NoComisiona, MantenerJunto, RTRIM(CCC) AS CCC
FROM Clientes WHERE Empresa = @empresa AND [Nº Cliente] = @cliente";

        internal const string SQL_VENDEDORES_GRUPO = @"
SELECT RTRIM(Contacto), RTRIM(GrupoProducto), RTRIM(Vendedor), Estado
FROM VendedoresClienteGrupoProducto WHERE Empresa = @empresa AND Cliente = @cliente";

        internal const string SQL_CONDICIONES = @"
SELECT RTRIM(p.Contacto), RTRIM(p.FormaPago), RTRIM(p.PlazosPago), p.ImporteMínimo, ISNULL(f.CCCObligatorio, 0)
FROM CondPagoClientes p LEFT JOIN FormasPago f ON f.Empresa = p.Empresa AND f.Número = p.FormaPago
WHERE p.Empresa = @empresa AND p.[Nº Cliente] = @cliente";

        internal const string SQL_DESCUENTOS = @"
SELECT RTRIM([Nº Producto]), RTRIM(GrupoProducto), RTRIM(Contacto), RTRIM(NºProveedor), RTRIM(ContactoProveedor), CantidadMínima, Descuento,
       Precio, RTRIM(Familia), FiltroProducto, AudienciaOferta, DescuentoPublico, CAST(FechaDesde AS datetime), CAST(FechaHasta AS datetime),
       Campana, RTRIM(SubGrupoProducto)
FROM DescuentosProducto WHERE Empresa = @empresa AND [Nº Cliente] = @cliente ORDER BY [Nº Orden]";

        internal const string SQL_OFERTAS = @"
SELECT RTRIM(Número), RTRIM(Familia), CantidadConPrecio, CantidadRegalo, Denegar, RTRIM(Contacto), FiltroProducto,
       CAST(FechaDesde AS datetime), CAST(FechaHasta AS datetime), RTRIM(SubGrupo)
FROM OfertasPermitidas WHERE Empresa = @empresa AND Cliente = @cliente ORDER BY NºOrden";

        internal const string SQL_ENLACES = @"
IF OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NOT NULL
    SELECT RTRIM(ClienteAnterior) FROM dbo.ClientesAnteriores WHERE Empresa = @empresa AND Cliente = @cliente";

        internal const string SQL_ORIGEN = @"
IF OBJECT_ID(N'dbo.ClientesOrigen', N'U') IS NOT NULL
    SELECT RTRIM(Origen) FROM dbo.ClientesOrigen WHERE Empresa = @empresa AND Cliente = @cliente";

        internal const string SQL_CREAR_ENLACE = @"
IF NOT EXISTS (SELECT 1 FROM dbo.ClientesAnteriores WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Cliente = @cliente AND ClienteAnterior = @anterior)
    INSERT INTO dbo.ClientesAnteriores (Empresa, Cliente, ClienteAnterior, Fecha, Usuario) VALUES (@empresa, @cliente, @anterior, GETDATE(), @usuario);";

        internal const string SQL_FICHA = @"
UPDATE Clientes SET
    Estado = COALESCE(@estado, Estado), Vendedor = COALESCE(@vendedor, Vendedor), IVA = COALESCE(@iva, IVA),
    Comentarios = COALESCE(@comentarios, Comentarios), DiasEnServir = COALESCE(@diasEnServir, DiasEnServir), Ruta = COALESCE(@ruta, Ruta),
    PeriodoFacturación = COALESCE(@periodo, PeriodoFacturación), NoComisiona = COALESCE(@noComisiona, NoComisiona),
    MantenerJunto = COALESCE(@mantenerJunto, MantenerJunto), Usuario = @usuario
WHERE Empresa = @empresa AND [Nº Cliente] = @cliente AND Contacto = @contacto";

        internal const string SQL_VENDEDOR_GRUPO = @"
IF EXISTS (SELECT 1 FROM VendedoresClienteGrupoProducto WHERE Empresa = @empresa AND Cliente = @cliente AND Contacto = @contacto AND GrupoProducto = @grupo)
    UPDATE VendedoresClienteGrupoProducto SET Vendedor = @vendedor, Estado = @estado, Usuario = @usuario
    WHERE Empresa = @empresa AND Cliente = @cliente AND Contacto = @contacto AND GrupoProducto = @grupo;
ELSE
    INSERT INTO VendedoresClienteGrupoProducto (Empresa, Cliente, Contacto, GrupoProducto, Vendedor, Estado, Usuario)
    VALUES (@empresa, @cliente, @contacto, @grupo, @vendedor, @estado, @usuario);";

        internal const string SQL_QUITAR_CONDICION = @"
DELETE FROM CondPagoClientes WHERE Empresa = @empresa AND [Nº Cliente] = @cliente AND Contacto = @contacto
    AND FormaPago = @forma AND PlazosPago = @plazos AND ImporteMínimo = @importe";

        internal const string SQL_PONER_CONDICION = @"
IF NOT EXISTS (SELECT 1 FROM CondPagoClientes WHERE Empresa = @empresa AND [Nº Cliente] = @cliente AND Contacto = @contacto
               AND FormaPago = @forma AND PlazosPago = @plazos AND ImporteMínimo = @importe)
    INSERT INTO CondPagoClientes (Empresa, [Nº Cliente], Contacto, FormaPago, PlazosPago, ImporteMínimo)
    VALUES (@empresa, @cliente, @contacto, @forma, @plazos, @importe);";

        internal const string SQL_DESCUENTO = @"
INSERT INTO DescuentosProducto (Empresa, [Nº Producto], GrupoProducto, [Nº Cliente], Contacto, NºProveedor, ContactoProveedor, CantidadMínima,
       Descuento, Precio, Familia, FiltroProducto, Usuario, [Fecha Modificación], AudienciaOferta, DescuentoPublico, FechaDesde, FechaHasta,
       Campana, SubGrupoProducto)
VALUES (@empresa, @producto, @grupo, @cliente, @contacto, @proveedor, @contactoProveedor, @cantidadMinima,
       @descuento, @precio, @familia, @filtro, @usuario, GETDATE(), @audiencia, @descuentoPublico, @desde, @hasta,
       @campana, @subgrupo);";

        internal const string SQL_OFERTA = @"
INSERT INTO OfertasPermitidas (Empresa, Número, Familia, CantidadConPrecio, CantidadRegalo, Denegar, Cliente, Contacto, FiltroProducto,
       Usuario, FechaModificación, FechaDesde, FechaHasta, SubGrupo)
VALUES (@empresa, @producto, @familia, @conPrecio, @regalo, @denegar, @cliente, @contacto, @filtro,
       @usuario, GETDATE(), @desde, @hasta, @subgrupo);";

        internal const string SQL_RASTRO = @"
INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario, Fecha) VALUES (N'ClienteCambioFiscal', @anterior, @nuevo, @usuario, GETDATE());";

        public async Task<bool> TablaExiste()
        {
            try
            {
                using (SqlConnection conexion = new SqlConnection(cadenaConexion))
                {
                    await conexion.OpenAsync().ConfigureAwait(false);
                    using (SqlCommand comando = new SqlCommand(SQL_TABLA, conexion))
                    {
                        return Convert.ToInt32(await comando.ExecuteScalarAsync().ConfigureAwait(false)) == 1;
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cambio de forma fiscal #876] No se ha podido comprobar dbo.ClientesAnteriores: {ex.Message}", ex));
                return false;
            }
        }

        public async Task<DatosCambioFiscal> Leer(string empresa, string anterior, string nuevo)
        {
            using (SqlConnection conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                return await LeerDatos(conexion, null, empresa, anterior, nuevo).ConfigureAwait(false);
            }
        }

        public async Task<DecisionCopiaCambioFiscal> Copiar(string empresa, string anterior, string nuevo, Func<DatosCambioFiscal, DecisionCopiaCambioFiscal> decidir, string usuario)
        {
            using (SqlConnection conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (SqlTransaction transaccion = conexion.BeginTransaction())
                {
                    using (SqlCommand bloqueo = Comando(@"DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
SELECT @r;", conexion, transaccion))
                    {
                        _ = bloqueo.Parameters.AddWithValue("@recurso", $"ClienteCambioFiscal:{empresa}:{nuevo}");
                        if (Convert.ToInt32(await bloqueo.ExecuteScalarAsync().ConfigureAwait(false)) < 0)
                        {
                            throw new TimeoutException($"Otra persona está copiando ahora mismo al cliente {nuevo}. Vuelva a intentarlo en un momento.");
                        }
                    }
                    DatosCambioFiscal datos = await LeerDatos(conexion, transaccion, empresa, anterior, nuevo).ConfigureAwait(false);
                    DecisionCopiaCambioFiscal decision = decidir(datos);
                    if (decision == null || !decision.Escribir)
                    {
                        transaccion.Rollback();
                        return decision;
                    }
                    string quien = Recortar(usuario, 30);
                    if (decision.CrearEnlace)
                    {
                        await Ejecutar(conexion, transaccion, SQL_CREAR_ENLACE, ("@empresa", empresa), ("@cliente", nuevo), ("@anterior", anterior), ("@usuario", quien)).ConfigureAwait(false);
                    }
                    await Aplicar(conexion, transaccion, empresa, nuevo, decision.Plan, quien).ConfigureAwait(false);
                    await Ejecutar(conexion, transaccion, SQL_RASTRO,
                        ("@anterior", $"{empresa}|{anterior} → {nuevo}"),
                        ("@nuevo", JsonConvert.SerializeObject(decision.Rastro)),
                        ("@usuario", quien)).ConfigureAwait(false);
                    transaccion.Commit();
                    return decision;
                }
            }
        }

        public async Task CrearEnlace(string empresa, string nuevo, string anterior, string usuario)
        {
            using (SqlConnection conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                await Ejecutar(conexion, null, SQL_CREAR_ENLACE, ("@empresa", empresa), ("@cliente", nuevo), ("@anterior", anterior), ("@usuario", Recortar(usuario, 30))).ConfigureAwait(false);
            }
        }

        private static async Task Aplicar(SqlConnection c, SqlTransaction t, string empresa, string cliente, PlanCopiaCambioFiscal plan, string usuario)
        {
            if (plan == null)
            {
                return;
            }
            foreach (CambioFichaCambioFiscal f in plan.Fichas)
            {
                await Ejecutar(c, t, SQL_FICHA, ("@empresa", empresa), ("@cliente", cliente), ("@contacto", f.Contacto),
                    ("@estado", f.Estado), ("@vendedor", f.Vendedor), ("@iva", f.Iva), ("@comentarios", f.Comentarios), ("@diasEnServir", f.DiasEnServir),
                    ("@ruta", f.Ruta), ("@periodo", f.PeriodoFacturacion), ("@noComisiona", f.NoComisiona), ("@mantenerJunto", f.MantenerJunto),
                    ("@usuario", usuario)).ConfigureAwait(false);
            }
            foreach (VendedorGrupoCambioFiscal v in plan.VendedoresGrupo)
            {
                await Ejecutar(c, t, SQL_VENDEDOR_GRUPO, ("@empresa", empresa), ("@cliente", cliente), ("@contacto", v.Contacto), ("@grupo", v.GrupoProducto),
                    ("@vendedor", v.Vendedor), ("@estado", v.Estado), ("@usuario", usuario)).ConfigureAwait(false);
            }
            foreach (CondPagoCambioFiscal q in plan.CondicionesAQuitar)
            {
                await Ejecutar(c, t, SQL_QUITAR_CONDICION, ("@empresa", empresa), ("@cliente", cliente), ("@contacto", q.Contacto),
                    ("@forma", q.FormaPago), ("@plazos", q.PlazosPago), ("@importe", q.ImporteMinimo)).ConfigureAwait(false);
            }
            foreach (CondPagoCambioFiscal p in plan.CondicionesAPoner)
            {
                await Ejecutar(c, t, SQL_PONER_CONDICION, ("@empresa", empresa), ("@cliente", cliente), ("@contacto", p.Contacto),
                    ("@forma", p.FormaPago), ("@plazos", p.PlazosPago), ("@importe", p.ImporteMinimo)).ConfigureAwait(false);
            }
            foreach (DescuentoCambioFiscal d in plan.Descuentos)
            {
                await Ejecutar(c, t, SQL_DESCUENTO, ("@empresa", empresa), ("@producto", d.Producto), ("@grupo", d.GrupoProducto), ("@cliente", cliente),
                    ("@contacto", d.Contacto), ("@proveedor", d.Proveedor), ("@contactoProveedor", d.ContactoProveedor), ("@cantidadMinima", d.CantidadMinima),
                    ("@descuento", d.Descuento), ("@precio", d.Precio), ("@familia", d.Familia), ("@filtro", d.FiltroProducto), ("@usuario", usuario),
                    ("@audiencia", d.AudienciaOferta), ("@descuentoPublico", d.DescuentoPublico), ("@desde", d.FechaDesde), ("@hasta", d.FechaHasta),
                    ("@campana", d.Campana), ("@subgrupo", d.SubGrupoProducto)).ConfigureAwait(false);
            }
            foreach (OfertaCambioFiscal o in plan.Ofertas)
            {
                await Ejecutar(c, t, SQL_OFERTA, ("@empresa", empresa), ("@producto", o.Producto), ("@familia", o.Familia), ("@conPrecio", o.CantidadConPrecio),
                    ("@regalo", o.CantidadRegalo), ("@denegar", o.Denegar), ("@cliente", cliente), ("@contacto", o.Contacto), ("@filtro", o.FiltroProducto),
                    ("@usuario", usuario), ("@desde", o.FechaDesde), ("@hasta", o.FechaHasta), ("@subgrupo", o.SubGrupo)).ConfigureAwait(false);
            }
        }

        private static async Task<DatosCambioFiscal> LeerDatos(SqlConnection c, SqlTransaction t, string empresa, string anterior, string nuevo)
        {
            DatosCambioFiscal datos = new DatosCambioFiscal
            {
                Anterior = await LeerFoto(c, t, empresa, anterior).ConfigureAwait(false),
                Nuevo = await LeerFoto(c, t, empresa, nuevo).ConfigureAwait(false)
            };
            datos.AnterioresDelNuevo = await LeerTextos(c, t, SQL_ENLACES, empresa, nuevo).ConfigureAwait(false);
            datos.OrigenNuevo = (await LeerTextos(c, t, SQL_ORIGEN, empresa, nuevo).ConfigureAwait(false)).FirstOrDefault();
            return datos;
        }

        private static async Task<FotoClienteCambioFiscal> LeerFoto(SqlConnection c, SqlTransaction t, string empresa, string cliente)
        {
            FotoClienteCambioFiscal foto = new FotoClienteCambioFiscal { Cliente = cliente };
            await Leer(c, t, SQL_FICHAS, empresa, cliente, r => foto.Fichas.Add(new FichaCambioFiscal
            {
                Contacto = Texto(r, 0),
                ClientePrincipal = !r.IsDBNull(1) && r.GetBoolean(1),
                Nombre = Texto(r, 2),
                Estado = r.IsDBNull(3) ? (short?)null : Convert.ToInt16(r.GetValue(3)),
                Vendedor = Texto(r, 4),
                Iva = Texto(r, 5),
                Comentarios = Texto(r, 6),
                DiasEnServir = Texto(r, 7),
                Ruta = Texto(r, 8),
                PeriodoFacturacion = Texto(r, 9),
                NoComisiona = r.IsDBNull(10) ? 0 : Convert.ToDecimal(r.GetValue(10)),
                MantenerJunto = !r.IsDBNull(11) && r.GetBoolean(11),
                Ccc = Texto(r, 12)
            })).ConfigureAwait(false);
            if (!foto.Fichas.Any())
            {
                return foto;
            }
            await Leer(c, t, SQL_VENDEDORES_GRUPO, empresa, cliente, r => foto.VendedoresGrupo.Add(new VendedorGrupoCambioFiscal
            {
                Contacto = Texto(r, 0),
                GrupoProducto = Texto(r, 1),
                Vendedor = Texto(r, 2),
                Estado = Convert.ToInt16(r.GetValue(3))
            })).ConfigureAwait(false);
            await Leer(c, t, SQL_CONDICIONES, empresa, cliente, r => foto.Condiciones.Add(new CondPagoCambioFiscal
            {
                Contacto = Texto(r, 0),
                FormaPago = Texto(r, 1),
                PlazosPago = Texto(r, 2),
                ImporteMinimo = Convert.ToDecimal(r.GetValue(3)),
                CccObligatorio = Convert.ToBoolean(r.GetValue(4))
            })).ConfigureAwait(false);
            await Leer(c, t, SQL_DESCUENTOS, empresa, cliente, r => foto.Descuentos.Add(new DescuentoCambioFiscal
            {
                Producto = Texto(r, 0),
                GrupoProducto = Texto(r, 1),
                Contacto = Texto(r, 2),
                Proveedor = Texto(r, 3),
                ContactoProveedor = Texto(r, 4),
                CantidadMinima = Convert.ToInt16(r.GetValue(5)),
                Descuento = Convert.ToDecimal(r.GetValue(6)),
                Precio = r.IsDBNull(7) ? (decimal?)null : Convert.ToDecimal(r.GetValue(7)),
                Familia = Texto(r, 8),
                FiltroProducto = Texto(r, 9),
                AudienciaOferta = Convert.ToByte(r.GetValue(10)),
                DescuentoPublico = r.IsDBNull(11) ? (decimal?)null : Convert.ToDecimal(r.GetValue(11)),
                FechaDesde = r.IsDBNull(12) ? (DateTime?)null : r.GetDateTime(12),
                FechaHasta = r.IsDBNull(13) ? (DateTime?)null : r.GetDateTime(13),
                Campana = Texto(r, 14),
                SubGrupoProducto = Texto(r, 15)
            })).ConfigureAwait(false);
            await Leer(c, t, SQL_OFERTAS, empresa, cliente, r => foto.Ofertas.Add(new OfertaCambioFiscal
            {
                Producto = Texto(r, 0),
                Familia = Texto(r, 1),
                CantidadConPrecio = Convert.ToInt16(r.GetValue(2)),
                CantidadRegalo = Convert.ToInt16(r.GetValue(3)),
                Denegar = Convert.ToBoolean(r.GetValue(4)),
                Contacto = Texto(r, 5),
                FiltroProducto = Texto(r, 6),
                FechaDesde = r.IsDBNull(7) ? (DateTime?)null : r.GetDateTime(7),
                FechaHasta = r.IsDBNull(8) ? (DateTime?)null : r.GetDateTime(8),
                SubGrupo = Texto(r, 9)
            })).ConfigureAwait(false);
            return foto;
        }

        private static async Task Leer(SqlConnection c, SqlTransaction t, string sql, string empresa, string cliente, Action<SqlDataReader> fila)
        {
            using (SqlCommand comando = Comando(sql, c, t))
            {
                _ = comando.Parameters.AddWithValue("@empresa", empresa);
                _ = comando.Parameters.AddWithValue("@cliente", cliente);
                using (SqlDataReader r = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await r.ReadAsync().ConfigureAwait(false))
                    {
                        fila(r);
                    }
                }
            }
        }

        private static async Task<List<string>> LeerTextos(SqlConnection c, SqlTransaction t, string sql, string empresa, string cliente)
        {
            List<string> textos = new List<string>();
            await Leer(c, t, sql, empresa, cliente, r => textos.Add(Texto(r, 0))).ConfigureAwait(false);
            return textos.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        private static async Task Ejecutar(SqlConnection c, SqlTransaction t, string sql, params (string Nombre, object Valor)[] parametros)
        {
            using (SqlCommand comando = Comando(sql, c, t))
            {
                foreach ((string nombre, object valor) in parametros)
                {
                    _ = comando.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
                }
                _ = await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        private static SqlCommand Comando(string sql, SqlConnection c, SqlTransaction t) => new SqlCommand(sql, c, t) { CommandTimeout = 60 };

        private static string Texto(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));

        private static string Recortar(string texto, int maximo) => string.IsNullOrEmpty(texto) || texto.Length <= maximo ? texto : texto.Substring(0, maximo);
    }
}
