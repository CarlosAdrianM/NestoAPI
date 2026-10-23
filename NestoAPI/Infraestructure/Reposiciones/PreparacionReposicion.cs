using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Contadores;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    // ------------------------------------------------------------------------------------------------------------------
    // DTOs
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>NestoAPI#553: lo que manda Nesto (o Ariadna) para crear una reposición y dejarla en preparación.</summary>
    public class CrearReposicionDTO
    {
        public string Empresa { get; set; } = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>Fecha de las líneas (la que elige el usuario en Nesto viejo). Por defecto, hoy.</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>
        /// Las líneas ya revisadas por el usuario. Si no vienen, se calculan con la propuesta
        /// (prdRellenarReposicionStock2), como hace el botón «Rellenar» de Nesto viejo.
        /// </summary>
        public List<LineaCrearReposicionDTO> Lineas { get; set; }
        /// <summary>
        /// NestoAPI#577 (corte 3a): con qué se crea, para la cabecera ReposicionesTraspasos: «Nesto», «Ariadna» o
        /// «Automatico» (el job). Opcional: si no viene, se deduce del usuario (con dominio, Nesto; sin dominio, Ariadna).
        /// Ver <see cref="HerramientasReposicion.Resolver"/>.
        /// </summary>
        public string Herramienta { get; set; }
    }

    public class LineaCrearReposicionDTO
    {
        public string Producto { get; set; }
        public int Cantidad { get; set; }
    }

    public class LineaReposicionEnPreparacionDTO : Models.PreparacionAlmacen.IConFotoProducto, Models.PreparacionAlmacen.IConProductosMismoHueco,
        Models.PreparacionAlmacen.IConReferenciaProveedor, Models.PreparacionAlmacen.IConCodigosBarras
    {
        /// <summary>PreExtrProducto.[Nº Orden]: con él se cambia la cantidad.</summary>
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>El código principal (el de la ficha, Productos.CodBarras).</summary>
        public string CodigoBarras { get; set; }
        /// <summary>
        /// Ariadna#22 (NestoAPI#605): todos los códigos activos del producto, el principal el primero, para que la salida
        /// guiada case por cualquiera. Lo rellena FichasProductoAlmacen.CompletarCodigos (una consulta por respuesta).
        /// </summary>
        public List<string> CodigosBarras { get; set; }
        public int Cantidad { get; set; }
        /// <summary>Stock actual del producto en el almacén de origen (lo que la tienda tiene para mandar).</summary>
        public int StockOrigen { get; set; }
        /// <summary>
        /// Origen con control de ubicaciones: el hueco (o huecos, «002/002/004 (3), 008/004/001 (2)») de donde sale lo
        /// reservado. Null en las tiendas o si no se ha podido reservar nada en un hueco.
        /// </summary>
        public string Hueco { get; set; }
        /// <summary>Origen con control de ubicaciones: alguna unidad no tiene hueco (no había libre o estaba pendiente de ubicar).</summary>
        public bool SinHueco { get; set; }
        public int UnidadesSinHueco { get; set; }
        /// <summary>NestoAPI#653: la foto del producto (FichasProductoAlmacen). Null si no tiene.</summary>
        public string UrlFoto { get; set; }
        /// <summary>NestoAPI#681: la referencia del proveedor principal (ProveedoresProducto.ReferenciaProv con Orden = 1), recortada. Null si no hay.</summary>
        public string ReferenciaProveedor { get; set; }
        /// <summary>
        /// Ariadna#47: los demás productos con stock en el (primer) hueco de donde sale lo reservado. Null sin hueco. Ver
        /// <see cref="Models.PreparacionAlmacen.IConProductosMismoHueco"/>.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<Models.PreparacionAlmacen.ProductoMismoHuecoDTO> ProductosMismoHueco { get; set; }
        /// <summary>Ariadna#47: el primer hueco reservado (el de <see cref="Hueco"/>), para comparar. No viaja.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public Models.PreparacionAlmacen.UbicacionAlmacenDTO HuecoDeReferencia { get; set; }
    }

    /// <summary>La reposición que un almacén tiene en preparación (como mucho una a la vez).</summary>
    public class ReposicionEnPreparacionDTO
    {
        public string Empresa { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>NestoAPI#600: el nombre del almacén de origen («Algete», Almacenes.Descripción).</summary>
        public string NombreOrigen { get; set; }
        /// <summary>NestoAPI#600: el nombre del almacén de destino («Alcobendas»).</summary>
        public string NombreDestino { get; set; }
        /// <summary>NestoAPI#600: el título tal como se enseña: «De Algete a Alcobendas».</summary>
        public string Titulo { get; set; }
        /// <summary>Diario de salida del origen (Almacenes.DiarioSalidaRep), donde viven las líneas hasta terminar.</summary>
        public string Diario { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
        /// <summary>
        /// Null mientras está en preparación (las tiendas, hasta Terminar). Desde un origen con control de ubicaciones
        /// (Algete) la API la deja ya cerrada, con su número de traspaso, lista para recoger en Ariadna (Recoger, REPO).
        /// </summary>
        public int? NumTraspaso { get; set; }
        public List<LineaReposicionEnPreparacionDTO> Lineas { get; set; } = new List<LineaReposicionEnPreparacionDTO>();
        public int Unidades => Lineas.Sum(l => l.Cantidad);
    }

    /// <summary>
    /// NestoAPI#600: GET api/Reposiciones/Ruta, los nombres de los dos almacenes y el título de la reposición
    /// («De Algete a Alcobendas»), una sola vez, para titular la pantalla antes de crearla (también si no hay nada que reponer).
    /// </summary>
    public class RutaReposicionDTO
    {
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string NombreOrigen { get; set; }
        public string NombreDestino { get; set; }
        public string Titulo { get; set; }
    }

    public class CambiarCantidadReposicionDTO
    {
        public int Cantidad { get; set; }
    }

    public class LineaTraspasoTerminadoDTO : Models.PreparacionAlmacen.IConFotoProducto
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Cantidad { get; set; }
        /// <summary>NestoAPI#653: la foto del producto (FichasProductoAlmacen). Null si no tiene.</summary>
        public string UrlFoto { get; set; }
    }

    /// <summary>Lo que ha deshecho DELETE api/Reposiciones/{numTraspaso}.</summary>
    public class ResultadoAnularReposicionDTO
    {
        public int NumTraspaso { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>Filas de PreExtrProducto borradas (salida y entrada).</summary>
        public int LineasBorradas { get; set; }
        /// <summary>Filas de Ubicaciones que han vuelto a su hueco (o a pendiente de ubicar).</summary>
        public int FilasUbicacionesDevueltas { get; set; }
        public List<string> Huecos { get; set; } = new List<string>();
    }

    /// <summary>Sugerencia 753: el cuerpo (opcional) de POST api/Reposiciones/EnPreparacion/Terminar.</summary>
    public class TerminarReposicionDTO
    {
        /// <summary>Los productos dados por «No está» en Ariadna. En una tienda sin ubicaciones, su stock se pone a 0.</summary>
        public List<string> NoEsta { get; set; }
    }

    public class ResultadoTerminarReposicionDTO
    {
        /// <summary>El número del traspaso (ContadoresGlobales.TraspasoAlmacén), el que ve el destino como pendiente de recibir.</summary>
        public int NumTraspaso { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        public string DiarioSalida { get; set; }
        public string DiarioEntrada { get; set; }
        public List<LineaTraspasoTerminadoDTO> Lineas { get; set; } = new List<LineaTraspasoTerminadoDTO>();
        public int Unidades => Lineas.Sum(l => l.Cantidad);
        /// <summary>Sugerencia 753: lo hecho después de terminar, una frase por cambio («Stock de Alcobendas a 0: 43763 (tenía 1).»).</summary>
        public List<string> Cambios { get; set; } = new List<string>();
        /// <summary>Sugerencia 753: los Cambios en un texto para enseñarlo tal cual. Null si no hay nada que contar.</summary>
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#622: lo que ha hecho el job al sumar la reposición de hoy a la que la tienda tenía sin enviar.</summary>
    public class ResultadoCompletarReposicionDTO
    {
        /// <summary>La reposición en preparación tal como queda (la de antes con lo de hoy sumado).</summary>
        public ReposicionEnPreparacionDTO Reposicion { get; set; }
        /// <summary>
        /// False si al ir a completarla la tienda ya la había enviado: entonces se ha creado una nueva, como siempre (y
        /// <see cref="LineasNuevas"/> son todas las suyas).
        /// </summary>
        public bool Completada { get; set; }
        /// <summary>Productos que no estaban y se han añadido.</summary>
        public int LineasNuevas { get; set; }
        /// <summary>Productos que ya estaban y se les ha subido la cantidad.</summary>
        public int LineasSubidas { get; set; }
        /// <summary>El día de la línea más antigua de la preparación que se ha completado (la del viaje que no se envió).</summary>
        public DateTime? PreparacionDesde { get; set; }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Repositorio
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Lo de Almacenes que hace falta para una reposición.</summary>
    public class DatosAlmacenReposicion
    {
        public string Numero { get; set; }
        public bool ControlUbicaciones { get; set; }
        public string DiarioEntradaRep { get; set; }
        public string DiarioSalidaRep { get; set; }
    }

    /// <summary>Una línea de PreExtrProducto de la reposición en preparación, con lo que se enseña de ella.</summary>
    public class FilaReposicionEnPreparacion
    {
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string CodigoBarras { get; set; }
        public int Cantidad { get; set; }
        public int StockOrigen { get; set; }
        /// <summary>PreExtrProducto.Almacén = el DESTINO (así lo graba Nesto viejo mientras se prepara).</summary>
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>Una fila de PreExtrProducto de un traspaso de reposición ya numerado (salida o entrada).</summary>
    public class FilaTraspasoReposicion
    {
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public string Almacen { get; set; }
        public string Diario { get; set; }
        public int Estado { get; set; }
    }

    /// <summary>
    /// NestoAPI#553: las escrituras de una reposición en preparación, una a una, en el orden en que las hace Nesto viejo
    /// (traza real ALC → ALG del 06/10/26, traspaso 80893, comentada en la issue). Cada método lleva la sentencia original.
    /// </summary>
    public interface IRepositorioPreparacionReposicion
    {
        /// <summary>Null si el almacén no existe en la empresa.</summary>
        Task<DatosAlmacenReposicion> LeerAlmacen(string empresa, string almacen);
        /// <summary>NestoAPI#600: el nombre del almacén (Almacenes.Descripción); null si no lo tiene.</summary>
        Task<string> LeerNombreAlmacen(string empresa, string almacen);
        /// <summary>Nesto viejo, antes de rellenar: <c>select [nºtraspaso] from inventarios where empresa='1 ' and almacen='ALC' and estado=3</c>.</summary>
        Task<bool> HayInventarioEnCurso(string empresa, string almacen);
        /// <summary>Las líneas del diario de salida del origen que van a OTRO almacén, sin número de traspaso todavía.</summary>
        Task<List<FilaReposicionEnPreparacion>> LeerLineasEnPreparacion(string empresa, string diario, string origen);
        /// <summary>
        /// Líneas del diario de salida con estado ≥ 0 que NO son del destino (Nesto viejo no termina si las hay). Con
        /// <paramref name="soloSinTraspaso"/>, solo las que aún no tienen número: en «General» de Algete conviven las salidas
        /// de otros traspasos por recoger, que se contabilizan aparte (Ariadna aparta las demás al terminar cada una).
        /// </summary>
        Task<int> ContarLineasDeOtroAlmacen(string empresa, string diario, string destino, bool soloSinTraspaso);
        /// <summary>Una línea de preparación. Devuelve las filas insertadas (0 si el producto no existe).</summary>
        Task<int> InsertarLineaPreparacion(string empresa, string diario, string origen, string destino, string producto, int cantidad,
            DateTime fecha, string texto, string vendedor, string usuario);
        Task<int> CambiarCantidad(string empresa, string diario, int numeroOrden, int cantidad);
        /// <summary>
        /// NestoAPI#622: suma <paramref name="unidades"/> a una línea en preparación (sin traspaso), sobre lo que tenga en ese
        /// momento: si la tienda la está bajando a la vez, no se pisa su cambio.
        /// </summary>
        Task<int> SumarCantidad(string empresa, string diario, int numeroOrden, int unidades);
        Task<int> BorrarLineasACero(string empresa, string diario, string destino);
        Task<int> AsignarTraspaso(string empresa, string diario, string destino, int numeroTraspaso, DateTime fecha);
        Task<int> ActualizarPedidosEspeciales(string empresa, string diario, string destino);
        Task<int> InsertarSalida(string empresa, string diario, string origen, string destino, DateTime fecha, string usuario);
        Task<int> MoverEntradaADiario(string empresa, string diario, string destino, string diarioEntrada);
        /// <summary>prdExtrProducto sobre el diario de salida, por su único punto de llamada.</summary>
        Task Contabilizar(string empresa, string diario, string usuario);
        Task<int> MarcarRestoEstado1(string empresa, string diario);
        Task<int> NuevoNumeroTraspaso();
        /// <summary>Las filas de PreExtrProducto de un traspaso (salida y entrada), bloqueadas hasta el final de la transacción.</summary>
        Task<List<FilaTraspasoReposicion>> LeerTraspaso(string empresa, int numeroTraspaso);
        /// <summary>¿Hay algo de ese traspaso en ExtractoProducto (salida o entrada contabilizada)?</summary>
        Task<bool> TraspasoContabilizado(string empresa, int numeroTraspaso);
        /// <summary>Lecturas de Ariadna (PreparacionEscaneos, REPO) de ese traspaso: si hay, alguien ya lo está recogiendo o recibiendo.</summary>
        Task<int> ContarLecturas(string empresa, int numeroTraspaso);
        Task<int> BorrarTraspaso(string empresa, int numeroTraspaso);
        Task<int> QuitarTraspasoDePedidosEspeciales(int numeroTraspaso);
        /// <summary>Todo lo de dentro va en una transacción: si algo falla, no queda nada a medias.</summary>
        Task<T> EnTransaccion<T>(Func<Task<T>> trabajo);
    }

    public class RepositorioPreparacionReposicionSql : IRepositorioPreparacionReposicion
    {
        private readonly NVEntities db;
        private readonly IServicioExtractoProducto extractos;
        private readonly INumeradorTraspasos numerador;

        public RepositorioPreparacionReposicionSql(NVEntities db, IServicioExtractoProducto extractos = null, INumeradorTraspasos numerador = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.extractos = extractos ?? new ServicioExtractoProducto();
            this.numerador = numerador ?? new NumeradorTraspasosSql();
        }

        internal const string SQL_ALMACEN = @"
SELECT RTRIM([Número]) AS Numero, CAST(ISNULL(ControlUbicaciones, 0) AS bit) AS ControlUbicaciones,
       RTRIM(DiarioEntradaRep) AS DiarioEntradaRep, RTRIM(DiarioSalidaRep) AS DiarioSalidaRep
FROM Almacenes WHERE Empresa = @p0 AND [Número] = @p1";

        // Nesto viejo (07:18:24): select [nºtraspaso] from inventarios where empresa='1 ' and almacen='ALC' and estado=3
        internal const string SQL_INVENTARIO_EN_CURSO = @"
SELECT COUNT(*) FROM Inventarios WHERE Empresa = @p0 AND [Almacén] = @p1 AND Estado = 3";

        // Lo que Nesto viejo pinta en la rejilla (vstReposiciónAlmacenAbajo, diario de salida) más el stock del origen
        // (índice extractoproducto11: Empresa, Número, Almacén, Cantidad). Sin NºTraspaso: en cuanto lo tiene ya no está
        // en preparación, está enviada.
        internal const string SQL_LINEAS_EN_PREPARACION = @"
SELECT p.[Nº Orden] AS NumeroOrden, RTRIM(p.[Número]) AS Producto, RTRIM(pr.Nombre) AS Nombre, RTRIM(pr.CodBarras) AS CodigoBarras,
       CAST(p.Cantidad AS int) AS Cantidad, RTRIM(p.[Almacén]) AS Almacen, p.Fecha, RTRIM(p.Usuario) AS Usuario,
       CAST(ISNULL((SELECT SUM(e.Cantidad) FROM ExtractoProducto e
                    WHERE e.Empresa = p.Empresa AND e.[Número] = p.[Número] AND e.[Almacén] = @p2), 0) AS int) AS StockOrigen
FROM PreExtrProducto p
     LEFT JOIN Productos pr ON pr.Empresa = p.Empresa AND pr.[Número] = p.[Número]
WHERE p.Empresa = @p0 AND p.Diario = @p1 AND p.[Almacén] <> @p2 AND p.Estado >= 0 AND ISNULL(p.[NºTraspaso], 0) = 0
ORDER BY pr.SubGrupo, p.[Número]";

        // Nesto viejo (07:32:16): select empresa from vstreposiciónAlmacénAbajo where diario='RepoAlcAlg' and empresa=1
        //                        and almacén<>'ALG' and estado>=0
        internal const string SQL_LINEAS_DE_OTRO_ALMACEN = @"
SELECT COUNT(*) FROM PreExtrProducto WHERE Empresa = @p0 AND Diario = @p1 AND [Almacén] <> @p2 AND Estado >= 0
  AND (@p3 = 0 OR ISNULL([NºTraspaso], 0) = 0)";

        // Nesto viejo (07:18:51), en dos pasos por _REPPROD que aquí sobran: Insert into preextrproducto(Vendedor,Empresa,
        // Diario,Número,Fecha,Texto,Almacén,Grupo,Cantidad,Delegación,[Forma Venta],[Asiento Automático]) select 'NV ','1 ',
        // '_REPPROD',producto,'05/10/26','Traspaso por reposición de almacén ALC a ALG','ALG',grupo,sum(cantidad),'ALC','TIE',1
        // … y después a 'RepoAlcAlg' con Estado 1. Nace en 3 (= impresa, en preparación): aquí no hay listado Crystal que
        // la pase de 1 a 3 (update vstreposiciónAlmacénabajo set estado=3 …). El Grupo sale de la ficha del producto, que es
        // lo que devuelve el procedimiento. Usuario explícito: el default de la columna sería la cuenta de máquina de la API.
        internal const string SQL_INSERTAR_LINEA = @"
INSERT INTO PreExtrProducto (Vendedor, Empresa, Diario, [Número], Fecha, Texto, [Almacén], Grupo, Cantidad, [Delegación],
                             [Forma Venta], [Asiento Automático], Estado, Usuario)
SELECT @p0, @p1, @p2, pr.[Número], @p3, @p4, @p5, pr.Grupo, @p6, @p7, 'TIE', 1, 3, @p8
FROM Productos pr WHERE pr.Empresa = @p1 AND pr.[Número] = @p9";

        // Nesto viejo (07:27:17): UPDATE "NV"."dbo"."PreExtrProducto" SET "CANTIDAD"=@P1 WHERE "CANTIDAD"=@P2 AND "EMPRESA"=@P3
        //                        AND "DIARIO"=@P4 AND "Nº ORDEN"=@P5 (tras exec prdCambiarCantidadReposicion @NºOrden, @Cantidad)
        internal const string SQL_CAMBIAR_CANTIDAD = @"
UPDATE PreExtrProducto SET Cantidad = @p3 WHERE Empresa = @p0 AND Diario = @p1 AND [Nº Orden] = @p2 AND ISNULL([NºTraspaso], 0) = 0";

        // NestoAPI#622: lo de hoy que se suma a una línea que ya estaba en la preparación sin enviar
        internal const string SQL_SUMAR_CANTIDAD = @"
UPDATE PreExtrProducto SET Cantidad = Cantidad + @p3 WHERE Empresa = @p0 AND Diario = @p1 AND [Nº Orden] = @p2 AND ISNULL([NºTraspaso], 0) = 0";

        // Nesto viejo (07:32:16): delete preextrproducto where diario='RepoAlcAlg' and empresa=1 and almacén='ALG' and estado>=0 and cantidad=0
        internal const string SQL_BORRAR_A_CERO = @"
DELETE FROM PreExtrProducto WHERE Empresa = @p0 AND Diario = @p1 AND [Almacén] = @p2 AND Estado >= 0 AND Cantidad = 0";

        // Nesto viejo: update preextrproducto set nºtraspaso=80893,fecha='06/10/26 9:32:16' where diario='RepoAlcAlg' and empresa=1 and almacén='ALG' and estado>=0
        internal const string SQL_ASIGNAR_TRASPASO = @"
UPDATE PreExtrProducto SET [NºTraspaso] = @p3, Fecha = @p4 WHERE Empresa = @p0 AND Diario = @p1 AND [Almacén] = @p2 AND Estado >= 0";

        // Nesto viejo: Update pedidosespeciales set NºTraspaso=e.[NºTraspaso] from preextrproducto as e inner join pedidosespeciales as p
        //              on e.número=p.número where p.almacen='ALG' and e.vendedor is null and e.diario='RepoAlcAlg' and e.empresa=1
        //              and e.almacén='ALG' and e.estado>=0   (las líneas «Compra», sin vendedor; PedidosEspeciales sin filtro de empresa, como él)
        internal const string SQL_PEDIDOS_ESPECIALES = @"
UPDATE pe SET pe.[NºTraspaso] = e.[NºTraspaso]
FROM PreExtrProducto e INNER JOIN PedidosEspeciales pe ON e.[Número] = pe.[Número]
WHERE pe.Almacen = @p2 AND e.Vendedor IS NULL AND e.Diario = @p1 AND e.Empresa = @p0 AND e.[Almacén] = @p2 AND e.Estado >= 0";

        // Nesto viejo: Insert into preextrproducto(Empresa,Diario,Número,Fecha,Texto,Almacén,Grupo,Cantidad,Delegación,[Forma Venta],
        //              [Asiento Automático],[nºtraspaso],vendedor) select Empresa,Diario,Número,'06/10/26 9:32:16',Texto,'ALC',Grupo,
        //              -Cantidad,Delegación,[Forma Venta],[Asiento Automático],[nºtraspaso],vendedor from vstreposiciónAlmacénAbajo
        //              where diario='RepoAlcAlg' and empresa=1 and almacén='ALG' and estado>=0
        // Estado 1 explícito (es el default de la columna, que es lo que le quedaba a Nesto viejo al no ponerlo). La vista es
        // PreExtrProducto con Productos y SubGruposProducto por INNER JOIN: el producto existe seguro porque lo exigió el INSERT.
        internal const string SQL_INSERTAR_SALIDA = @"
INSERT INTO PreExtrProducto (Empresa, Diario, [Número], Fecha, Texto, [Almacén], Grupo, Cantidad, [Delegación], [Forma Venta],
                             [Asiento Automático], [NºTraspaso], Vendedor, Estado, Usuario)
SELECT p.Empresa, p.Diario, p.[Número], @p3, p.Texto, @p4, p.Grupo, -p.Cantidad, p.[Delegación], p.[Forma Venta],
       p.[Asiento Automático], p.[NºTraspaso], p.Vendedor, 1, @p5
FROM PreExtrProducto p
WHERE p.Empresa = @p0 AND p.Diario = @p1 AND p.[Almacén] = @p2 AND p.Estado >= 0";

        // Nesto viejo: update preextrproducto set diario='PendRepo2' where diario='RepoAlcAlg' and empresa=1 and almacén='ALG' and estado>=0
        internal const string SQL_MOVER_ENTRADA = @"
UPDATE PreExtrProducto SET Diario = @p3 WHERE Empresa = @p0 AND Diario = @p1 AND [Almacén] = @p2 AND Estado >= 0";

        // Nesto viejo: update preextrproducto set estado=1 where número<>'R ' and empresa=1 and diario='RepoAlcAlg' (0 filas en la traza:
        // tras contabilizar no queda nada; se conserva por fidelidad)
        internal const string SQL_MARCAR_RESTO = @"
UPDATE PreExtrProducto SET Estado = 1 WHERE [Número] <> 'R' AND Empresa = @p0 AND Diario = @p1";

        // DELETE api/Reposiciones/{n}: el traspaso entero, bloqueado
        internal const string SQL_TRASPASO = @"
SELECT p.[Nº Orden] AS NumeroOrden, RTRIM(p.[Número]) AS Producto, CAST(p.Cantidad AS int) AS Cantidad, RTRIM(p.[Almacén]) AS Almacen,
       RTRIM(p.Diario) AS Diario, CAST(p.Estado AS int) AS Estado
FROM PreExtrProducto p WITH (UPDLOCK, HOLDLOCK)
WHERE p.Empresa = @p0 AND p.[NºTraspaso] = @p1";

        internal const string SQL_TRASPASO_CONTABILIZADO = @"
SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM ExtractoProducto WHERE Empresa = @p0 AND [NºTraspaso] = @p1) THEN 1 ELSE 0 END AS bit)";

        internal const string SQL_LECTURAS_DEL_TRASPASO = @"
SELECT COUNT(*) FROM PreparacionEscaneos WHERE Empresa = @p0 AND TipoOrigen = 'REPO' AND NumeroOrigen = @p1";

        internal const string SQL_BORRAR_TRASPASO = @"
DELETE FROM PreExtrProducto WHERE Empresa = @p0 AND [NºTraspaso] = @p1";

        // Lo contrario de SQL_PEDIDOS_ESPECIALES (sin filtro de empresa, como aquel)
        internal const string SQL_QUITAR_TRASPASO_PEDIDOS_ESPECIALES = @"
UPDATE PedidosEspeciales SET [NºTraspaso] = NULL WHERE [NºTraspaso] = @p0";

        public async Task<DatosAlmacenReposicion> LeerAlmacen(string empresa, string almacen)
        {
            return await db.Database.SqlQuery<DatosAlmacenReposicion>(SQL_ALMACEN, Char("@p0", empresa, 3), Char("@p1", almacen, 3))
                .FirstOrDefaultAsync().ConfigureAwait(false);
        }

        public Task<string> LeerNombreAlmacen(string empresa, string almacen)
        {
            return RepositorioPreparacionAlmacen.LeerNombreAlmacen(db.Database, empresa, almacen);
        }

        public async Task<bool> HayInventarioEnCurso(string empresa, string almacen)
        {
            int enCurso = await db.Database.SqlQuery<int>(SQL_INVENTARIO_EN_CURSO, Char("@p0", empresa, 3), Char("@p1", almacen, 3))
                .SingleAsync().ConfigureAwait(false);
            return enCurso > 0;
        }

        public Task<List<FilaReposicionEnPreparacion>> LeerLineasEnPreparacion(string empresa, string diario, string origen)
        {
            return db.Database.SqlQuery<FilaReposicionEnPreparacion>(SQL_LINEAS_EN_PREPARACION,
                Char("@p0", empresa, 3), Char("@p1", diario, 10), Char("@p2", origen, 3)).ToListAsync();
        }

        public Task<int> ContarLineasDeOtroAlmacen(string empresa, string diario, string destino, bool soloSinTraspaso)
        {
            return db.Database.SqlQuery<int>(SQL_LINEAS_DE_OTRO_ALMACEN,
                Char("@p0", empresa, 3), Char("@p1", diario, 10), Char("@p2", destino, 3),
                new SqlParameter("@p3", SqlDbType.Bit) { Value = soloSinTraspaso }).SingleAsync();
        }

        public Task<int> InsertarLineaPreparacion(string empresa, string diario, string origen, string destino, string producto, int cantidad,
            DateTime fecha, string texto, string vendedor, string usuario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR_LINEA,
                Char("@p0", vendedor, 3), Char("@p1", empresa, 3), Char("@p2", diario, 10),
                new SqlParameter("@p3", SqlDbType.DateTime) { Value = fecha },
                Char("@p4", texto, 50), Char("@p5", destino, 3),
                new SqlParameter("@p6", SqlDbType.SmallInt) { Value = (short)cantidad },
                Char("@p7", origen, 3),
                new SqlParameter("@p8", SqlDbType.VarChar, 30) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) },
                Char("@p9", producto, 15));
        }

        public Task<int> CambiarCantidad(string empresa, string diario, int numeroOrden, int cantidad)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_CAMBIAR_CANTIDAD,
                Char("@p0", empresa, 3), Char("@p1", diario, 10),
                new SqlParameter("@p2", SqlDbType.Int) { Value = numeroOrden },
                new SqlParameter("@p3", SqlDbType.SmallInt) { Value = (short)cantidad });
        }

        public Task<int> SumarCantidad(string empresa, string diario, int numeroOrden, int unidades)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_SUMAR_CANTIDAD,
                Char("@p0", empresa, 3), Char("@p1", diario, 10),
                new SqlParameter("@p2", SqlDbType.Int) { Value = numeroOrden },
                new SqlParameter("@p3", SqlDbType.SmallInt) { Value = (short)unidades });
        }

        public Task<int> BorrarLineasACero(string empresa, string diario, string destino)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_BORRAR_A_CERO, Char("@p0", empresa, 3), Char("@p1", diario, 10), Char("@p2", destino, 3));
        }

        public Task<int> AsignarTraspaso(string empresa, string diario, string destino, int numeroTraspaso, DateTime fecha)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_ASIGNAR_TRASPASO,
                Char("@p0", empresa, 3), Char("@p1", diario, 10), Char("@p2", destino, 3),
                new SqlParameter("@p3", SqlDbType.Int) { Value = numeroTraspaso },
                new SqlParameter("@p4", SqlDbType.DateTime) { Value = fecha });
        }

        public Task<int> ActualizarPedidosEspeciales(string empresa, string diario, string destino)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_PEDIDOS_ESPECIALES, Char("@p0", empresa, 3), Char("@p1", diario, 10), Char("@p2", destino, 3));
        }

        public Task<int> InsertarSalida(string empresa, string diario, string origen, string destino, DateTime fecha, string usuario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR_SALIDA,
                Char("@p0", empresa, 3), Char("@p1", diario, 10), Char("@p2", destino, 3),
                new SqlParameter("@p3", SqlDbType.DateTime) { Value = fecha },
                Char("@p4", origen, 3),
                new SqlParameter("@p5", SqlDbType.VarChar, 30) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) });
        }

        public Task<int> MoverEntradaADiario(string empresa, string diario, string destino, string diarioEntrada)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_MOVER_ENTRADA,
                Char("@p0", empresa, 3), Char("@p1", diario, 10), Char("@p2", destino, 3), Char("@p3", diarioEntrada, 10));
        }

        /// <summary>
        /// prdExtrProducto llamado por la API (SUSER_NAME = NUEVAVISION\RDS2016$) con el usuario de la tienda en @Usuario:
        /// como SUSER_NAME lleva «$», el procedimiento respeta @Usuario para ExtractoProducto.Usuario y Ubicaciones.Usuario.
        /// En cambio, @UsuarioParams = stuff(system_user, 1, patindex('%\%', @Usuario), '') = «RDS2016$», que no tiene
        /// AlmacénPedidoVta → @Almacen, @DiarioRepo y @DiarioEntradaRepo quedan NULL. Consecuencia para un diario de salida de
        /// tienda (solo líneas negativas, almacén sin ControlUbicaciones, sin pasillo): todos los bloques de ubicaciones se
        /// saltan, igual que desde Nesto viejo (allí se saltan porque @diario = @diariorepo; aquí porque la comparación con
        /// NULL es UNKNOWN, y además filtran ControlUbicaciones = 1). Lo que SÍ corre, igual en los dos casos: prdFechaEsVálida,
        /// prdComprobarStockNegativo (si la tienda se quedara en negativo, 400 con su texto), el precio medio si el diario lo
        /// recalcula y el INSERT en ExtractoProducto. Verificado sobre el cuerpo del procedimiento el 06/10/26.
        /// </summary>
        public Task Contabilizar(string empresa, string diario, string usuario)
        {
            return extractos.ContabilizarDiario(db, empresa, diario, UsuarioAuditoriaHelper.ParaAuditoria(usuario));
        }

        public Task<int> MarcarRestoEstado1(string empresa, string diario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_MARCAR_RESTO, Char("@p0", empresa, 3), Char("@p1", diario, 10));
        }

        public Task<int> NuevoNumeroTraspaso()
        {
            return numerador.Siguiente(db);
        }

        public Task<List<FilaTraspasoReposicion>> LeerTraspaso(string empresa, int numeroTraspaso)
        {
            return db.Database.SqlQuery<FilaTraspasoReposicion>(SQL_TRASPASO, Char("@p0", empresa, 3), Traspaso("@p1", numeroTraspaso)).ToListAsync();
        }

        public Task<bool> TraspasoContabilizado(string empresa, int numeroTraspaso)
        {
            return db.Database.SqlQuery<bool>(SQL_TRASPASO_CONTABILIZADO, Char("@p0", empresa, 3), Traspaso("@p1", numeroTraspaso)).SingleAsync();
        }

        public Task<int> ContarLecturas(string empresa, int numeroTraspaso)
        {
            return db.Database.SqlQuery<int>(SQL_LECTURAS_DEL_TRASPASO, Char("@p0", empresa, 3), Traspaso("@p1", numeroTraspaso)).SingleAsync();
        }

        public Task<int> BorrarTraspaso(string empresa, int numeroTraspaso)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_BORRAR_TRASPASO, Char("@p0", empresa, 3), Traspaso("@p1", numeroTraspaso));
        }

        public Task<int> QuitarTraspasoDePedidosEspeciales(int numeroTraspaso)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_QUITAR_TRASPASO_PEDIDOS_ESPECIALES, Traspaso("@p0", numeroTraspaso));
        }

        private static SqlParameter Traspaso(string nombre, int numero)
        {
            return new SqlParameter(nombre, SqlDbType.Int) { Value = numero };
        }

        public async Task<T> EnTransaccion<T>(Func<Task<T>> trabajo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    T resultado = await trabajo().ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
                catch (Exception ex)
                {
                    try
                    {
                        transaccion.Rollback();
                    }
                    catch (Exception)
                    {
                        // El ROLLBACK del procedimiento ya la ha deshecho
                    }
                    if (ex is NestoBusinessException)
                    {
                        throw;
                    }
                    // Los RAISERROR de prdExtrProducto (stock negativo, fecha no permitida…) son avisos para el usuario, no fallos
                    SqlException sql = ex as SqlException ?? ex.InnerException as SqlException;
                    if (sql != null && sql.Class >= 11 && sql.Class <= 16)
                    {
                        throw new NestoBusinessException(ex.Message, ex);
                    }
                    throw;
                }
            }
        }

        private static SqlParameter Char(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, SqlDbType.Char, longitud) { Value = (object)valor ?? DBNull.Value };
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Servicio
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>NestoAPI#577 (corte 3b): el origen ya tiene una reposición en preparación (409). El job la distingue de un fallo.</summary>
    public class ReposicionYaEnPreparacionException : NestoBusinessException
    {
        public ReposicionYaEnPreparacionException(string mensaje) : base(mensaje)
        {
            StatusCode = HttpStatusCode.Conflict;
        }
    }

    /// <summary>NestoAPI#577 (corte 3b): la propuesta no tiene nada que mandar (400). Para el job es un resultado normal.</summary>
    public class ReposicionVaciaException : NestoBusinessException
    {
        public ReposicionVaciaException(string mensaje) : base(mensaje)
        {
        }
    }

    public interface IServicioPreparacionReposicion
    {
        /// <summary>
        /// Crea la reposición. Desde una tienda la deja en preparación (hasta Terminar); desde un origen con control de
        /// ubicaciones (Algete) reserva los huecos y la cierra ya, sin contabilizar, lista para recoger en Ariadna.
        /// 409 si hay inventario en curso o ya hay una en preparación.
        /// </summary>
        Task<ReposicionEnPreparacionDTO> Crear(CrearReposicionDTO peticion, IPrincipal usuario);
        /// <summary>La que el origen tiene en preparación, o null.</summary>
        Task<ReposicionEnPreparacionDTO> LeerEnPreparacion(string empresa, string origen);

        /// <summary>NestoAPI#600: nombres y título de la ruta origen → destino. Null si alguno de los almacenes no existe.</summary>
        Task<RutaReposicionDTO> LeerRuta(string empresa, string origen, string destino);
        /// <summary>Baja (o deja a 0) la cantidad de una línea. 400 si se intenta subir.</summary>
        Task<ReposicionEnPreparacionDTO> CambiarCantidad(string empresa, string origen, int numeroOrden, int cantidad, IPrincipal usuario);
        /// <summary>Da la reposición por preparada: contabiliza la salida del origen y deja la entrada pendiente de recibir en el destino.</summary>
        Task<ResultadoTerminarReposicionDTO> Terminar(string empresa, string origen, IPrincipal usuario);
        /// <summary>
        /// Sugerencia 753: lo mismo y, ya confirmada, en una tienda sin control de ubicaciones el stock de cada producto de
        /// <paramref name="noEsta"/> (los dados por «No está» en Ariadna; solo los de esta reposición) a 0 en la tienda. Un
        /// fallo ahí no deshace la reposición: se avisa en Cambios y Mensaje.
        /// </summary>
        Task<ResultadoTerminarReposicionDTO> Terminar(string empresa, string origen, IPrincipal usuario, IReadOnlyCollection<string> noEsta);
        /// <summary>
        /// Anula un traspaso cerrado que aún no se ha recogido ni contabilizado: devuelve lo reservado a los huecos y borra
        /// la salida y la entrada. 409 si ya tiene lecturas en Ariadna o está contabilizado; 404 si no existe.
        /// </summary>
        Task<ResultadoAnularReposicionDTO> Anular(string empresa, int numeroTraspaso, IPrincipal usuario);
        /// <summary>
        /// Sugerencia 581: una reposición PUNTUAL de una tienda a Algete con solo lo que dice la petición: se crea y se termina
        /// de una vez (número, salida contabilizada y entrada pendiente de recibir en Algete), sin tocar la del calendario.
        /// </summary>
        Task<ResultadoTerminarReposicionDTO> CrearPuntual(CrearReposicionDTO peticion, IPrincipal usuario);
        /// <summary>
        /// Sugerencia 581 (Carlos, 13/10/26): si hoy se puede hacer la puntual de <paramref name="origen"/> a Algete y, si no, por
        /// qué (sin viaje hoy, la de hoy sin rellenar o sin enviar, o el repartidor ya se la ha llevado). Para que Nesto lo diga
        /// antes de empezar y sepa si puede marcar la recogida.
        /// </summary>
        Task<EstadoReposicionPuntualDTO> LeerEstadoPuntual(string empresa, string origen);
        /// <summary>
        /// Sugerencia 581 (Carlos, 13/10/26): el repartidor ya se ha llevado la reposición de hoy de la tienda a Algete. Desde ese
        /// momento no se puede hacer otra puntual hoy. 409 si hoy no hay viaje, la de hoy no está rellena o enviada, ya estaba
        /// marcada o falta el script; 403 sin permiso.
        /// </summary>
        Task<EstadoReposicionPuntualDTO> MarcarRecogida(MarcarRecogidaReposicionDTO peticion, IPrincipal usuario);
    }

    /// <summary>
    /// NestoAPI#553: crear, preparar y terminar una reposición desde la API haciendo EXACTAMENTE las escrituras de Nesto
    /// viejo (traza real de Paloma, ALC → ALG, 06/10/26; datos del traspaso 80885 de Andre, ALG → ALC, 05/10/26). Mientras
    /// se prepara, la reposición son las líneas del diario de SALIDA del origen con Almacén = destino y cantidad positiva.
    /// Al cerrar: número de traspaso, salida negativa en el origen y entrada movida al diario de ENTRADA del destino,
    /// pendiente de recibir (RecepcionReposicionesAlmacen).
    ///
    /// <para>Dos caminos según el origen:</para>
    /// <list type="bullet">
    /// <item>Tienda (sin control de ubicaciones): Crear la deja en preparación; la tienda baja cantidades y Terminar la
    /// cierra Y contabiliza la salida (prdExtrProducto), como Nesto viejo.</item>
    /// <item>Algete (con control de ubicaciones): Crear, en una transacción, crea las líneas, reserva los huecos por la
    /// puerta única de Ubicaciones (NestoAPI#594) y la cierra SIN contabilizar: queda «por salir» en Recoger de Ariadna
    /// (REPO), que la contabiliza al terminar la recogida. NestoAPI#651: hasta entonces las reservas siguen en el hueco
    /// (estado 4, enlazadas al traspaso) y pasan a -4 al contabilizar, para que Ubicaciones cuadre siempre con el extracto. Se anula con <see cref="Anular"/> mientras nadie la recoja.</item>
    /// </list>
    /// </summary>
    public class ServicioPreparacionReposicion : IServicioPreparacionReposicion, IDisposable
    {
        public const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";
        public const string VENDEDOR_VENTA = "NV";
        public const string FORMA_VENTA = "TIE";

        internal static readonly string[] ALMACENES_REPOSICION =
        {
            Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS
        };

        private readonly IRepositorioPreparacionReposicion repositorio;
        private readonly Func<string, string, string, DateTime?, Task<List<LineaPropuestaReposicionDTO>>> propuesta;
        private readonly Func<string, bool, IUbicacionesReposicion> ubicaciones;
        private readonly Func<string, string, string> almacenDelUsuario;
        private readonly Func<DateTime> ahora;
        private readonly IRegistroReposicionesTraspasos registro;
        private readonly Func<string, string, DateTime, Task<IDisposable>> bloquear;
        private readonly IViajesReposicionPuntual viajes;
        private readonly CalculadoraFechaReposicion calculadora;
        private readonly IStockLibreReposicionPuntual stockLibre;
        /// <summary>Sugerencia 753: el stock a 0 de lo que «No está». Null: no se toca (pruebas que no lo miran).</summary>
        private readonly IServicioStockACeroNoEsta stockACero;
        private readonly NVEntities dbPropio;

        public ServicioPreparacionReposicion() : this(new NVEntities())
        {
        }

        public ServicioPreparacionReposicion(NVEntities db)
            : this(new RepositorioPreparacionReposicionSql(db),
                (empresa, origen, destino, corte) => new ServicioPropuestaReposicion(db).CalcularPropuesta(empresa, origen, destino, corte),
                (origen, control) => UbicacionesReposicion.Para(origen, control, () => new PuertaUbicacionesSql(db)),
                (empresa, usuario) => Controllers.ParametrosUsuarioController.LeerParametro(empresa, usuario, CLAVE_ALMACEN_USUARIO),
                () => DateTime.Now,
                new RegistroReposicionesTraspasosSql(db),
                (empresa, origen, dia) => new RepositorioReposicionAutomaticaSql(db).Bloquear(empresa, origen, dia),
                new ViajesReposicionPuntualSql(db),
                new StockLibreReposicionPuntualSql(db),
                new CalculadoraFechaReposicion(),
                new ServicioStockACeroNoEsta())
        {
            dbPropio = db;
        }

        /// <param name="propuesta">(empresa, origen, destino, corte) → la propuesta de prdRellenarReposicionStock2.</param>
        /// <param name="ubicaciones">(origen, controlUbicaciones) → qué hacer con los huecos del origen.</param>
        /// <param name="almacenDelUsuario">(empresa, usuario sin dominio) → su AlmacénPedidoVta.</param>
        /// <param name="registro">NestoAPI#577: la cabecera ReposicionesTraspasos. Null: no se apunta (pruebas que no la miran).</param>
        /// <param name="bloquear">
        /// Sugerencia 581: (empresa, origen, día) → el mismo applock que el job de las reposiciones automáticas, para que una
        /// puntual no se cruce con el job escribiendo en el mismo diario de salida. Null: sin bloqueo (pruebas).
        /// </param>
        /// <param name="viajes">
        /// Sugerencia 581: el calendario, si el job ya ha rellenado la de hoy y si el repartidor se la ha llevado. Null: viaje
        /// todos los laborables, ya relleno y sin recoger (pruebas que no lo miran).
        /// </param>
        /// <param name="stockLibre">Sugerencia 581: lo que la tienda puede mandar sin dejar sin servir sus pedidos. Null: no se mira (pruebas).</param>
        /// <param name="calculadora">Sugerencia 581: festivos y viaje de hoy, como el job. Null: sin festivos (pruebas).</param>
        /// <param name="stockACero">Sugerencia 753: el stock a 0 de lo que «No está», después de terminar. Null: no se toca (pruebas).</param>
        internal ServicioPreparacionReposicion(IRepositorioPreparacionReposicion repositorio,
            Func<string, string, string, DateTime?, Task<List<LineaPropuestaReposicionDTO>>> propuesta,
            Func<string, bool, IUbicacionesReposicion> ubicaciones,
            Func<string, string, string> almacenDelUsuario,
            Func<DateTime> ahora,
            IRegistroReposicionesTraspasos registro = null,
            Func<string, string, DateTime, Task<IDisposable>> bloquear = null,
            IViajesReposicionPuntual viajes = null,
            IStockLibreReposicionPuntual stockLibre = null,
            CalculadoraFechaReposicion calculadora = null,
            IServicioStockACeroNoEsta stockACero = null)
        {
            this.stockACero = stockACero;
            this.viajes = viajes ?? ViajesReposicionPuntualSinControl.Instancia;
            this.calculadora = calculadora ?? new CalculadoraFechaReposicion((dia, almacen) => false);
            this.stockLibre = stockLibre ?? SinStockLibreReposicionPuntual.Instancia;
            this.registro = registro ?? SinRegistroReposicionesTraspasos.Instancia;
            this.bloquear = bloquear ?? ((e, o, d) => Task.FromResult<IDisposable>(new SinBloqueo()));
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.propuesta = propuesta ?? throw new ArgumentNullException(nameof(propuesta));
            this.ubicaciones = ubicaciones ?? throw new ArgumentNullException(nameof(ubicaciones));
            this.almacenDelUsuario = almacenDelUsuario ?? ((e, u) => null);
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public Task<ReposicionEnPreparacionDTO> Crear(CrearReposicionDTO peticion, IPrincipal usuario)
        {
            return Crear(peticion, usuario, corte: null);
        }

        /// <param name="corte">
        /// NestoAPI#577: el instante de corte con el que se rellena (el job del corte 3b: día + HoraCierre del calendario).
        /// Sin líneas en la petición, de los pedidos pendientes solo cuentan los creados antes. Null: a mano (todos).
        /// </param>
        public async Task<ReposicionEnPreparacionDTO> Crear(CrearReposicionDTO peticion, IPrincipal usuario, DateTime? corte)
        {
            RutaReposicion ruta = await ComprobarRuta(peticion, usuario).ConfigureAwait(false);
            string empresa = ruta.Empresa;
            string origen = ruta.Origen;
            string destino = ruta.Destino;
            string herramienta = ruta.Herramienta;
            DatosAlmacenReposicion almacenOrigen = ruta.AlmacenOrigen;
            DatosAlmacenReposicion almacenDestino = ruta.AlmacenDestino;
            IUbicacionesReposicion huecos = ubicaciones(origen, almacenOrigen.ControlUbicaciones);
            string diario = almacenOrigen.DiarioSalidaRep;

            List<FilaReposicionEnPreparacion> existentes = await repositorio.LeerLineasEnPreparacion(empresa, diario, origen).ConfigureAwait(false);
            if (existentes.Any())
            {
                string destinoExistente = existentes.First().Almacen;
                throw new ReposicionYaEnPreparacionException($"Ya hay una reposición en preparación de {origen} a {destinoExistente} con {existentes.Count} líneas: " +
                    "termínala antes de crear otra.");
            }

            List<LineaCrearReposicionDTO> lineas = await LineasACrear(peticion, empresa, origen, destino, corte).ConfigureAwait(false);
            if (!lineas.Any())
            {
                throw new ReposicionVaciaException($"No hay nada que reponer de {origen} a {destino}.");
            }

            DateTime fecha = peticion.Fecha ?? ahora().Date;
            string texto = Texto(origen, destino);
            string quien = UsuarioAuditoriaHelper.Resolver(usuario, null);
            bool cerrarYa = almacenOrigen.ControlUbicaciones;
            List<FilaReposicionEnPreparacion> creadas = null;
            ResumenUbicaciones reservas = null;
            int? numeroTraspaso = await repositorio.EnTransaccion<int?>(async () =>
            {
                foreach (LineaCrearReposicionDTO linea in lineas)
                {
                    int insertadas = await repositorio.InsertarLineaPreparacion(empresa, diario, origen, destino, linea.Producto, linea.Cantidad,
                        fecha, texto, VENDEDOR_VENTA, quien).ConfigureAwait(false);
                    if (insertadas != 1)
                    {
                        throw new NestoBusinessException($"El producto {linea.Producto} no existe en la empresa {empresa}. No se ha creado la reposición.");
                    }
                }
                // Con su [Nº Orden]: es el NºOrdenRepo de la reserva (prdUbicarReposicion lo enlaza con la línea de ENTRADA)
                creadas = await repositorio.LeerLineasEnPreparacion(empresa, diario, origen).ConfigureAwait(false);
                reservas = await huecos.Reservar(empresa, origen, creadas
                    .Select(f => new LineaReservaReposicion { NumeroOrdenEntrada = f.NumeroOrden, Producto = f.Producto, Cantidad = f.Cantidad })
                    .ToList(), quien).ConfigureAwait(false);
                var cabecera = new CabeceraReposicionTraspaso
                {
                    Empresa = empresa, Origen = origen, Destino = destino, Herramienta = herramienta,
                    UsuarioCreacion = quien, FechaCreacion = ahora(), FechaCorte = corte
                };
                if (!cerrarYa)
                {
                    // NestoAPI#577: la tienda la prepara sin número (hasta Terminar): la cabecera también nace sin él
                    await registro.Crear(cabecera).ConfigureAwait(false);
                    return null;
                }
                int numero = await CerrarPreparacion(empresa, origen, destino, diario, almacenDestino, creadas.Where(f => f.Cantidad > 0).ToList(),
                    huecos, quien, contabilizar: false).ConfigureAwait(false);
                cabecera.NumTraspaso = numero;
                await registro.Crear(cabecera).ConfigureAwait(false);
                return numero;
            }).ConfigureAwait(false);

            if (!cerrarYa)
            {
                return await LeerEnPreparacion(empresa, origen, almacenOrigen).ConfigureAwait(false);
            }
            // Ya no está «en preparación» (tiene traspaso): se devuelve lo que se ha creado, con dónde está cada cosa
            ReposicionEnPreparacionDTO creada = Dto(empresa, origen, diario, creadas, reservas);
            creada.NumTraspaso = numeroTraspaso;
            return await ConNombres(creada).ConfigureAwait(false);
        }

        /// <summary>
        /// NestoAPI#622 (Carlos, 14/10/26): el job a la hora de corte, cuando la tienda aún tiene sin enviar la reposición de
        /// un viaje anterior (con rutas diarias, a menudo). Antes se omitía la de hoy y la tienda veía la vieja como si fuera
        /// la del día, sin las necesidades nuevas. Ahora se SUMA a la que hay:
        /// <list type="bullet">
        /// <item>La propuesta (prdRellenarReposicionStock2 con el corte de hoy) ya cuenta lo que está en preparación como en
        /// camino hacia el destino y comprometido en el origen (estado A del procedimiento), así que lo que devuelve es lo
        /// que FALTA además de lo preparado: los productos que no están se añaden y a los que están se les suma. Nunca se
        /// baja nada: si la tienda bajó una línea, la propuesta vuelve a pedir lo que falte y la línea sube hasta ahí.</item>
        /// <item>Cabecera nueva del job con el corte de hoy (para que ese día quede tratado), sin número y enlazada a la de
        /// la preparación (<see cref="IRegistroReposicionesTraspasos.CrearCompletando"/>): Terminar numera las dos.</item>
        /// <item>Las líneas nuevas llevan la fecha de hoy; las que ya estaban conservan la suya.</item>
        /// </list>
        /// No se completa (sigue el 409 <see cref="ReposicionYaEnPreparacionException"/>, y el job la omite y avisa) si la
        /// preparación abierta es hacia OTRO destino o el origen tiene control de ubicaciones (Algete: la API nunca deja una
        /// abierta, la crea ya cerrada y reservada; una abierta allí es de Nesto viejo y completarla exigiría reservar huecos).
        /// Una puntual (sugerencia 581) nunca está abierta: nace y se termina de una vez, así que no puede ser la que se
        /// completa (y mientras haya una abierta la puntual da 409). Si al llegar aquí la tienda ya la ha enviado, se crea
        /// una nueva como siempre.
        /// </summary>
        public async Task<ResultadoCompletarReposicionDTO> Completar(CrearReposicionDTO peticion, IPrincipal usuario, DateTime corte)
        {
            RutaReposicion ruta = await ComprobarRuta(peticion, usuario).ConfigureAwait(false);
            string empresa = ruta.Empresa;
            string origen = ruta.Origen;
            string destino = ruta.Destino;
            string diario = ruta.AlmacenOrigen.DiarioSalidaRep;

            List<FilaReposicionEnPreparacion> existentes = await repositorio.LeerLineasEnPreparacion(empresa, diario, origen).ConfigureAwait(false);
            if (!existentes.Any())
            {
                // La han enviado mientras tanto: la de hoy es una normal
                ReposicionEnPreparacionDTO creada = await Crear(peticion, usuario, corte).ConfigureAwait(false);
                return new ResultadoCompletarReposicionDTO { Reposicion = creada, Completada = false, LineasNuevas = creada?.Lineas?.Count ?? 0 };
            }
            string otroDestino = existentes.Select(f => f.Almacen?.Trim()).FirstOrDefault(a => !string.Equals(a, destino, StringComparison.OrdinalIgnoreCase));
            if (otroDestino != null)
            {
                throw new ReposicionYaEnPreparacionException($"Ya hay una reposición en preparación de {origen} a {otroDestino} con {existentes.Count} líneas: " +
                    $"la de {destino} no se puede sumar a ella. Termínala para que se pueda rellenar la de {destino}.");
            }
            if (ruta.AlmacenOrigen.ControlUbicaciones)
            {
                throw new ReposicionYaEnPreparacionException($"Ya hay una reposición en preparación de {origen} a {destino} con {existentes.Count} líneas " +
                    $"(hecha en Nesto viejo): desde {origen} no se completa sola. Termínala o bórrala en Nesto viejo.");
            }

            List<LineaCrearReposicionDTO> faltan = await LineasACrear(peticion, empresa, origen, destino, corte).ConfigureAwait(false);
            Dictionary<string, FilaReposicionEnPreparacion> yaEstan = existentes
                .Where(f => !string.IsNullOrWhiteSpace(f.Producto))
                .GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(f => f.NumeroOrden).First(), StringComparer.OrdinalIgnoreCase);
            var nuevas = new List<LineaCrearReposicionDTO>();
            var subidas = new List<(FilaReposicionEnPreparacion Fila, int Unidades)>();
            foreach (LineaCrearReposicionDTO linea in faltan)
            {
                if (yaEstan.TryGetValue(linea.Producto, out FilaReposicionEnPreparacion fila))
                {
                    if (fila.Cantidad + linea.Cantidad > short.MaxValue)
                    {
                        throw new NestoBusinessException($"No se puede sumar {linea.Cantidad} a {fila.Producto}: pasaría de {short.MaxValue}.");
                    }
                    subidas.Add((fila, linea.Cantidad));
                }
                else
                {
                    nuevas.Add(linea);
                }
            }

            DateTime desde = existentes.Min(f => f.Fecha).Date;
            DateTime fecha = ahora().Date;
            string texto = Texto(origen, destino);
            string quien = UsuarioAuditoriaHelper.Resolver(usuario, null);
            _ = await repositorio.EnTransaccion(async () =>
            {
                foreach (LineaCrearReposicionDTO linea in nuevas)
                {
                    int insertadas = await repositorio.InsertarLineaPreparacion(empresa, diario, origen, destino, linea.Producto, linea.Cantidad,
                        fecha, texto, VENDEDOR_VENTA, quien).ConfigureAwait(false);
                    if (insertadas != 1)
                    {
                        throw new NestoBusinessException($"El producto {linea.Producto} no existe en la empresa {empresa}. No se ha sumado nada.");
                    }
                }
                foreach ((FilaReposicionEnPreparacion fila, int unidades) in subidas)
                {
                    int sumadas = await repositorio.SumarCantidad(empresa, diario, fila.NumeroOrden, unidades).ConfigureAwait(false);
                    if (sumadas != 1)
                    {
                        throw Conflicto($"La reposición de {origen} a {destino} ha cambiado mientras tanto (¿se ha enviado?): no se ha sumado nada.");
                    }
                }
                await registro.CrearCompletando(new CabeceraReposicionTraspaso
                {
                    Empresa = empresa, Origen = origen, Destino = destino, Herramienta = ruta.Herramienta,
                    UsuarioCreacion = quien, FechaCreacion = ahora(), FechaCorte = corte
                }, desde).ConfigureAwait(false);
                return nuevas.Count + subidas.Count;
            }).ConfigureAwait(false);

            return new ResultadoCompletarReposicionDTO
            {
                Reposicion = await LeerEnPreparacion(empresa, origen, ruta.AlmacenOrigen).ConfigureAwait(false),
                Completada = true,
                LineasNuevas = nuevas.Count,
                LineasSubidas = subidas.Count,
                PreparacionDesde = desde
            };
        }

        /// <summary>Lo que Crear y Completar comprueban de la petición antes de leer nada de la preparación.</summary>
        private sealed class RutaReposicion
        {
            public string Empresa;
            public string Origen;
            public string Destino;
            public string Herramienta;
            public DatosAlmacenReposicion AlmacenOrigen;
            public DatosAlmacenReposicion AlmacenDestino;
        }

        private async Task<RutaReposicion> ComprobarRuta(CrearReposicionDTO peticion, IPrincipal usuario)
        {
            if (peticion == null)
            {
                throw new NestoBusinessException("Faltan los datos de la reposición.");
            }
            string empresa = Empresa(peticion.Empresa);
            string origen = Almacen(peticion.Origen);
            string destino = Almacen(peticion.Destino);
            ComprobarAlmacenes(origen, destino);
            // NestoAPI#577: antes de escribir nada, para que una herramienta que no existe sea un 400 limpio
            string herramienta = HerramientasReposicion.Resolver(peticion.Herramienta, usuario);

            DatosAlmacenReposicion almacenOrigen = await AlmacenConDiario(empresa, origen, a => a.DiarioSalidaRep, "de salida").ConfigureAwait(false);
            ComprobarPuedeEscribir(usuario, empresa, origen, almacenOrigen.ControlUbicaciones);
            // El destino tiene que poder recibirla (su DiarioEntradaRep): mejor saberlo ahora que al terminar
            DatosAlmacenReposicion almacenDestino = await AlmacenConDiario(empresa, destino, a => a.DiarioEntradaRep, "de entrada").ConfigureAwait(false);

            if (await repositorio.HayInventarioEnCurso(empresa, origen).ConfigureAwait(false))
            {
                throw Conflicto($"El almacén {origen} tiene un inventario en curso: termínalo antes de crear una reposición.");
            }
            return new RutaReposicion
            {
                Empresa = empresa, Origen = origen, Destino = destino, Herramienta = herramienta,
                AlmacenOrigen = almacenOrigen, AlmacenDestino = almacenDestino
            };
        }

        public async Task<ReposicionEnPreparacionDTO> LeerEnPreparacion(string empresa, string origen)
        {
            empresa = Empresa(empresa);
            origen = Almacen(origen);
            DatosAlmacenReposicion almacen = await repositorio.LeerAlmacen(empresa, origen).ConfigureAwait(false);
            if (almacen == null || string.IsNullOrWhiteSpace(almacen.DiarioSalidaRep))
            {
                return null;
            }
            return await LeerEnPreparacion(empresa, origen, almacen).ConfigureAwait(false);
        }

        public async Task<RutaReposicionDTO> LeerRuta(string empresa, string origen, string destino)
        {
            empresa = Empresa(empresa);
            origen = Almacen(origen);
            destino = Almacen(destino);
            ComprobarAlmacenes(origen, destino);
            if (await repositorio.LeerAlmacen(empresa, origen).ConfigureAwait(false) == null
                || await repositorio.LeerAlmacen(empresa, destino).ConfigureAwait(false) == null)
            {
                return null;
            }
            string nombreOrigen = await repositorio.LeerNombreAlmacen(empresa, origen).ConfigureAwait(false);
            string nombreDestino = await repositorio.LeerNombreAlmacen(empresa, destino).ConfigureAwait(false);
            return new RutaReposicionDTO
            {
                Origen = origen,
                Destino = destino,
                NombreOrigen = nombreOrigen,
                NombreDestino = nombreDestino,
                Titulo = ServicioPropuestaReposicion.TituloReposicion(origen, nombreOrigen, destino, nombreDestino)
            };
        }

        public async Task<ReposicionEnPreparacionDTO> CambiarCantidad(string empresa, string origen, int numeroOrden, int cantidad, IPrincipal usuario)
        {
            empresa = Empresa(empresa);
            origen = Almacen(origen);
            if (cantidad < 0 || cantidad > short.MaxValue)
            {
                throw new NestoBusinessException($"La cantidad {cantidad} no vale: tiene que estar entre 0 y {short.MaxValue}.");
            }
            DatosAlmacenReposicion almacen = await AlmacenConDiario(empresa, origen, a => a.DiarioSalidaRep, "de salida").ConfigureAwait(false);
            ComprobarPuedeEscribir(usuario, empresa, origen, almacen.ControlUbicaciones);
            IUbicacionesReposicion huecos = ubicaciones(origen, almacen.ControlUbicaciones);
            List<FilaReposicionEnPreparacion> filas = await repositorio.LeerLineasEnPreparacion(empresa, almacen.DiarioSalidaRep, origen).ConfigureAwait(false);
            FilaReposicionEnPreparacion fila = filas.FirstOrDefault(f => f.NumeroOrden == numeroOrden);
            if (fila == null)
            {
                throw new NestoBusinessException($"La línea {numeroOrden} no está en la reposición en preparación de {origen}.")
                {
                    StatusCode = HttpStatusCode.NotFound
                };
            }
            if (cantidad > fila.Cantidad)
            {
                // Nesto viejo (prdCambiarCantidadReposicion): «No se puede poner una cantidad mayor a la impresa»
                throw new NestoBusinessException($"No se puede poner una cantidad mayor que la preparada ({fila.Cantidad}) en {fila.Producto}: " +
                    "si hace falta más, crea otra reposición cuando termine esta.");
            }
            string quien = UsuarioAuditoriaHelper.Resolver(usuario, null);
            _ = await repositorio.EnTransaccion(async () =>
            {
                // Nesto viejo primero devuelve el sobrante al hueco (prdCambiarCantidadReposicion) y después cambia la línea
                _ = await huecos.DevolverSobrante(empresa, origen, numeroOrden, fila.Producto, cantidad, quien).ConfigureAwait(false);
                int cambiadas = await repositorio.CambiarCantidad(empresa, almacen.DiarioSalidaRep, numeroOrden, cantidad).ConfigureAwait(false);
                if (cambiadas != 1)
                {
                    throw Conflicto($"La línea {numeroOrden} ha cambiado mientras tanto: vuelve a cargar la reposición.");
                }
                return cambiadas;
            }).ConfigureAwait(false);
            return await LeerEnPreparacion(empresa, origen, almacen).ConfigureAwait(false);
        }

        public Task<ResultadoTerminarReposicionDTO> Terminar(string empresa, string origen, IPrincipal usuario)
        {
            return Terminar(empresa, origen, usuario, null);
        }

        public async Task<ResultadoTerminarReposicionDTO> Terminar(string empresa, string origen, IPrincipal usuario, IReadOnlyCollection<string> noEsta)
        {
            empresa = Empresa(empresa);
            origen = Almacen(origen);
            // (1) Nesto viejo: select DiarioEntradaRep from almacenes where número='ALG'; select controlubicaciones,DiarioSalidaRep … 'ALC'
            DatosAlmacenReposicion almacenOrigen = await AlmacenConDiario(empresa, origen, a => a.DiarioSalidaRep, "de salida").ConfigureAwait(false);
            ComprobarPuedeEscribir(usuario, empresa, origen, almacenOrigen.ControlUbicaciones);
            IUbicacionesReposicion huecos = ubicaciones(origen, almacenOrigen.ControlUbicaciones);
            string diario = almacenOrigen.DiarioSalidaRep;

            List<FilaReposicionEnPreparacion> filas = await repositorio.LeerLineasEnPreparacion(empresa, diario, origen).ConfigureAwait(false);
            if (!filas.Any())
            {
                throw Conflicto($"No hay ninguna reposición en preparación en {origen}.");
            }
            List<string> destinos = filas.Select(f => f.Almacen?.Trim()).Distinct().ToList();
            if (destinos.Count > 1)
            {
                throw Conflicto($"En el diario {diario} de {origen} hay líneas para varios almacenes ({string.Join(", ", destinos)}): " +
                    "no se puede terminar desde aquí.");
            }
            string destino = destinos.Single();
            DatosAlmacenReposicion almacenDestino = await AlmacenConDiario(empresa, destino, a => a.DiarioEntradaRep, "de entrada").ConfigureAwait(false);
            List<FilaReposicionEnPreparacion> conCantidad = filas.Where(f => f.Cantidad > 0).ToList();
            if (!conCantidad.Any())
            {
                throw Conflicto($"Todas las líneas de la reposición de {origen} a {destino} están a 0: no hay nada que mandar.");
            }
            string quien = UsuarioAuditoriaHelper.Resolver(usuario, null);

            int numeroTraspaso;
            // NestoAPI#693: el diario contabilizado y lo que manda la tienda van en UN solo evento de «promesa en riesgo»
            using (EventosPromesaRiesgo.Ambito())
            {
                numeroTraspaso = await repositorio.EnTransaccion(async () =>
                {
                    int numero = await CerrarPreparacion(empresa, origen, destino, diario, almacenDestino, conCantidad, huecos, quien,
                        contabilizar: true).ConfigureAwait(false);
                    // NestoAPI#577: el número va a la cabecera abierta de la reposición (si la creó la API; Nesto viejo no la tiene)
                    await registro.NumerarYMarcarPreparada(empresa, origen, destino, numero, quien).ConfigureAwait(false);
                    return numero;
                }).ConfigureAwait(false);
                AnotarPromesaRiesgo(empresa, origen, destino, numeroTraspaso, conCantidad);
            }

            var terminada = new ResultadoTerminarReposicionDTO
            {
                NumTraspaso = numeroTraspaso,
                Origen = origen,
                Destino = destino,
                DiarioSalida = diario,
                DiarioEntrada = almacenDestino.DiarioEntradaRep,
                Lineas = conCantidad.Select(f => new LineaTraspasoTerminadoDTO { Producto = f.Producto, Nombre = f.Nombre, Cantidad = f.Cantidad }).ToList()
            };
            await PonerACeroLoQueNoEsta(terminada, empresa, origen, almacenOrigen, filas, noEsta, quien).ConfigureAwait(false);
            return terminada;
        }

        /// <summary>
        /// Sugerencia 753 (Carlos, 23/10/26): ya confirmada la reposición, en una tienda (sin control de ubicaciones) lo dado por
        /// «No está» deja su stock a 0 en la tienda: no hay hueco, así que no estar quiere decir que no hay ninguno. Solo los
        /// productos de esta reposición (las líneas en preparación, también las bajadas a 0). Nunca lanza.
        /// </summary>
        private async Task PonerACeroLoQueNoEsta(ResultadoTerminarReposicionDTO terminada, string empresa, string origen,
            DatosAlmacenReposicion almacenOrigen, List<FilaReposicionEnPreparacion> filas, IReadOnlyCollection<string> noEsta, string quien)
        {
            if (stockACero == null || noEsta == null || noEsta.Count == 0 || almacenOrigen.ControlUbicaciones)
            {
                return;
            }
            var deLaReposicion = new HashSet<string>(filas.Select(f => f.Producto?.Trim()).Where(p => !string.IsNullOrEmpty(p)),
                StringComparer.OrdinalIgnoreCase);
            List<string> productos = noEsta.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())
                .Where(deLaReposicion.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (productos.Count == 0)
            {
                return;
            }
            ResultadoStockACero resultado = await stockACero.PonerACero(empresa, origen, terminada.NumTraspaso, productos, quien)
                .ConfigureAwait(false);
            if (resultado?.Cambios != null && resultado.Cambios.Count > 0)
            {
                terminada.Cambios.AddRange(resultado.Cambios);
                terminada.Mensaje = string.Join(" ", terminada.Cambios);
            }
        }

        /// <summary>
        /// NestoAPI#693: una reposición que sale de una tienda hacia Algete dice cuánto manda de cada producto («Reina mandó 1
        /// de 3 en la reposición 81094»): los pedidos que contaban con más se recalculan y, si ya no llegan, se avisa. Lo que
        /// sale de Algete hacia una tienda también mueve el stock de Algete, pero sin esa causa (lo cubre el diario). Nunca lanza.
        /// </summary>
        internal static void AnotarPromesaRiesgo(string empresa, string origen, string destino, int numeroTraspaso,
            IEnumerable<FilaReposicionEnPreparacion> mandadas)
        {
            List<FilaReposicionEnPreparacion> filas = (mandadas ?? Enumerable.Empty<FilaReposicionEnPreparacion>())
                .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Producto)).ToList();
            Dictionary<string, CausaPromesaRiesgo> causas = null;
            if (string.Equals(destino?.Trim(), Constantes.Almacenes.ALGETE, StringComparison.OrdinalIgnoreCase))
            {
                causas = filas.GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => new CausaPromesaRiesgo
                    {
                        Tienda = origen?.Trim(),
                        Mandadas = g.Sum(f => f.Cantidad),
                        Traspaso = numeroTraspaso
                    }, StringComparer.OrdinalIgnoreCase);
            }
            EventosPromesaRiesgo.Anotar(empresa, filas.Select(f => f.Producto),
                $"reposición {numeroTraspaso} de {CalculadoraFechaEntregaAgencia.NombreAlmacen(origen)} a {CalculadoraFechaEntregaAgencia.NombreAlmacen(destino)}", causas);
        }

        public async Task<ResultadoAnularReposicionDTO> Anular(string empresa, int numeroTraspaso, IPrincipal usuario)
        {
            empresa = Empresa(empresa);
            if (!EscrituraSoloAlmacenAttribute.PuedeEscribir(usuario))
            {
                throw new UnauthorizedAccessException("Una reposición ya cerrada solo la pueden anular Almacén y Dirección.");
            }
            string quien = UsuarioAuditoriaHelper.Resolver(usuario, null);
            return await repositorio.EnTransaccion(async () =>
            {
                List<FilaTraspasoReposicion> filas = await repositorio.LeerTraspaso(empresa, numeroTraspaso).ConfigureAwait(false) ?? new List<FilaTraspasoReposicion>();
                List<FilaTraspasoReposicion> salidas = filas.Where(f => f.Cantidad < 0).ToList();
                bool contabilizado = await repositorio.TraspasoContabilizado(empresa, numeroTraspaso).ConfigureAwait(false);
                if (contabilizado)
                {
                    throw Conflicto($"El traspaso {numeroTraspaso} ya está contabilizado (salida o entrada): no se puede anular.");
                }
                if (!salidas.Any())
                {
                    throw new NestoBusinessException($"No hay ninguna reposición por salir con el traspaso {numeroTraspaso}.") { StatusCode = HttpStatusCode.NotFound };
                }
                int lecturas = await repositorio.ContarLecturas(empresa, numeroTraspaso).ConfigureAwait(false);
                if (lecturas > 0)
                {
                    throw Conflicto($"El traspaso {numeroTraspaso} ya se está recogiendo en Ariadna ({lecturas} lecturas): no se puede anular.");
                }
                string origen = salidas.First().Almacen;
                string destino = filas.Where(f => f.Cantidad > 0).Select(f => f.Almacen).FirstOrDefault();
                DatosAlmacenReposicion almacenOrigen = await repositorio.LeerAlmacen(empresa, origen).ConfigureAwait(false);
                IUbicacionesReposicion huecos = ubicaciones(origen, almacenOrigen?.ControlUbicaciones == true);

                // (1) lo reservado vuelve a su hueco (4 → 0/2; -4 → 0/2 si se cerró antes de NestoAPI#651)
                ResumenUbicaciones devuelto = await huecos.AnularSalida(empresa, origen, numeroTraspaso, quien).ConfigureAwait(false);
                // (2) lo contrario de «update pedidosespeciales set NºTraspaso=…»
                _ = await repositorio.QuitarTraspasoDePedidosEspeciales(numeroTraspaso).ConfigureAwait(false);
                // (3) fuera la salida y la entrada
                int borradas = await repositorio.BorrarTraspaso(empresa, numeroTraspaso).ConfigureAwait(false);
                if (borradas != filas.Count)
                {
                    throw Conflicto($"El traspaso {numeroTraspaso} ha cambiado mientras tanto ({borradas} líneas borradas, {filas.Count} esperadas): vuelve a intentarlo.");
                }
                // (4) NestoAPI#577: y su cabecera (el traspaso ya no existe)
                await registro.Borrar(empresa, numeroTraspaso).ConfigureAwait(false);
                return new ResultadoAnularReposicionDTO
                {
                    NumTraspaso = numeroTraspaso,
                    Origen = origen,
                    Destino = destino,
                    LineasBorradas = borradas,
                    FilasUbicacionesDevueltas = devuelto.Movimientos.Count,
                    Huecos = devuelto.Huecos.ToList()
                };
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Sugerencia 581 (Paloma, 13/10/26; opción elegida por Carlos: «reposición manual pequeña»). Pasada la hora de cierre,
        /// la reposición del calendario ya está rellena (y a menudo enviada); si alguien le pide a la tienda que mande un producto
        /// concreto a Algete antes de que pase el repartidor, la tienda hace una reposición APARTE con solo eso. Carlos: que sea
        /// una excepción puntual; NO seguir rellenando la automática hasta que pase el repartidor, porque se caería el cálculo de
        /// fechas de entrega prometidas.
        ///
        /// <para>Cómo es:</para>
        /// <list type="bullet">
        /// <item>Solo de una tienda (sin control de ubicaciones) a Algete, con las líneas que vienen (sin propuesta). La puede
        /// hacer quien tiene esa tienda como AlmacénPedidoVta (su tienda y ninguna otra), Almacén o Dirección; no hace falta estar
        /// en «UsuariosRellenarReposicionManual» (eso es para rellenar a mano la del calendario).</item>
        /// <item>De una vez, en una transacción: las líneas, el número de traspaso, la salida contabilizada de la tienda y la
        /// entrada pendiente de recibir en Algete, exactamente como Terminar. Así nunca queda nada «en preparación» que haga que
        /// el job se salte el siguiente viaje (YaEnPreparacion). Algete la recibe como cualquier otra (Ariadna Entradas, Nesto
        /// Recibir).</item>
        /// <item>Si la tienda tiene una reposición en preparación (la del calendario sin enviar), 409: que la envíe antes.</item>
        /// <item>Carlos, 13/10/26 (409 con el motivo, <see cref="ReglasReposicionPuntual"/>): solo los días con viaje de la tienda a
        /// Algete (si no, no la recoge nadie), cuando el job ya ha rellenado la de hoy (antes, esos productos pueden ir en la
        /// automática) y hasta que la tienda marca que el repartidor se la ha llevado (<see cref="MarcarRecogida"/>).</item>
        /// <item>Cada producto tiene que tener stock en la tienda (400 con la lista; prdExtrProducto también lo impediría).</item>
        /// <item>Cabecera ReposicionesTraspasos con Herramienta «Puntual», número, quién y cuándo, ya preparada y sin FechaCorte:
        /// no cubre el viaje del calendario (el job la descarta en BuscarTraspasoQueCubre), no es «abierta» (Terminar no la
        /// numera) y HayCabeceraDelDia solo mira las del job.</item>
        /// <item>Con el applock del job (origen + día): no se cruza con la automática escribiendo en el mismo diario.</item>
        /// </list>
        ///
        /// <para>Fechas de entrega (corrección de Carlos, 13/10/26): la puntual ADELANTA la fecha de los pedidos que esperan
        /// esos productos, como cualquier reposición enviada. En cuanto se termina, sus unidades salen del stock de la tienda y
        /// quedan en PreExtrProducto con número de traspaso (PendienteReposicion de la calculadora): «ya vienen de camino» y
        /// salen el laborable siguiente (CalculadoraFechaEntregaAgencia, ORIGEN_EN_CAMINO), en vez de esperar a la siguiente
        /// automática de la tienda. Lo ya prometido (CabPedidoVta.FechaEntregaAgenciaPrometida) no se reescribe; los pedidos
        /// nuevos y el correo de modificación ya dan la fecha nueva. La propuesta del siguiente viaje las cuenta como stock de
        /// Algete (no se mandan dos veces).</para>
        /// </summary>
        public async Task<ResultadoTerminarReposicionDTO> CrearPuntual(CrearReposicionDTO peticion, IPrincipal usuario)
        {
            if (peticion == null)
            {
                throw new NestoBusinessException("Faltan los datos de la reposición puntual.");
            }
            string empresa = Empresa(peticion.Empresa);
            string origen = Almacen(peticion.Origen);
            string destino = string.IsNullOrWhiteSpace(peticion.Destino) ? Constantes.Almacenes.ALGETE : Almacen(peticion.Destino);
            ComprobarAlmacenes(origen, destino);
            if (destino != Constantes.Almacenes.ALGETE)
            {
                throw new NestoBusinessException("Una reposición puntual solo puede ir de una tienda a Algete.");
            }
            List<LineaCrearReposicionDTO> lineas = Consolidar(peticion.Lineas);
            if (!lineas.Any())
            {
                throw new NestoBusinessException("Pon al menos un producto con cantidad para la reposición puntual.");
            }
            if (lineas.Any(l => l.Cantidad > short.MaxValue))
            {
                throw new NestoBusinessException($"La cantidad de una línea no puede pasar de {short.MaxValue}.");
            }

            DatosAlmacenReposicion almacenOrigen = await AlmacenConDiario(empresa, origen, a => a.DiarioSalidaRep, "de salida").ConfigureAwait(false);
            if (almacenOrigen.ControlUbicaciones)
            {
                throw new NestoBusinessException($"Las reposiciones puntuales son de una tienda a Algete: desde {origen} no se pueden hacer.");
            }
            if (!PuedeEscribir(usuario, empresa, origen))
            {
                throw new UnauthorizedAccessException($"Una reposición puntual de {origen} solo la puede hacer la gente de esa tienda (o Almacén y Dirección). " +
                    "Tu usuario no tiene ese almacén como almacén de pedidos.");
            }
            DateTime momento = ahora();
            // Sugerencia 581 (Carlos, 13/10/26): solo los días con viaje, con la de hoy ya rellena y hasta que la tienda marca que
            // el repartidor se la ha llevado → 409. Lo de «enviada» se mira dentro del bloqueo (el job puede estar rellenándola).
            EstadoReposicionPuntualDTO estado = await CalcularEstadoPuntual(empresa, origen, destino, momento, enPreparacion: false).ConfigureAwait(false);
            if (!estado.SePuede)
            {
                throw Conflicto(estado.Motivo);
            }
            DatosAlmacenReposicion almacenDestino = await AlmacenConDiario(empresa, destino, a => a.DiarioEntradaRep, "de entrada").ConfigureAwait(false);
            if (await repositorio.HayInventarioEnCurso(empresa, origen).ConfigureAwait(false))
            {
                throw Conflicto($"El almacén {origen} tiene un inventario en curso: termínalo antes de mandar nada.");
            }
            IUbicacionesReposicion huecos = ubicaciones(origen, false);
            string diario = almacenOrigen.DiarioSalidaRep;
            string texto = Texto(origen, destino);
            string quien = UsuarioAuditoriaHelper.Resolver(usuario, null);
            List<FilaReposicionEnPreparacion> creadas = null;
            int numeroTraspaso;

            using (await bloquear(empresa, origen, momento.Date).ConfigureAwait(false))
            {
                // Dentro del bloqueo: el job puede haber rellenado la del calendario mientras tanto
                List<FilaReposicionEnPreparacion> existentes = await repositorio.LeerLineasEnPreparacion(empresa, diario, origen).ConfigureAwait(false);
                if (existentes.Any())
                {
                    throw new ReposicionYaEnPreparacionException($"{ReglasReposicionPuntual.TEXTO_SIN_ENVIAR} {origen} tiene una reposición a " +
                        $"{existentes.First().Almacen?.Trim()} en preparación ({existentes.Count} líneas) sin enviar: termínala antes de hacer una puntual.");
                }
                // Y que no se haya marcado la recogida mientras tanto (MarcarRecogida usa el mismo bloqueo)
                ViajeReposicionDelDia viajeAhora = await viajes.LeerViajeDelDia(empresa, origen, destino, estado.CierreHoy.Value.Date).ConfigureAwait(false);
                if (viajeAhora?.FechaRecogida != null)
                {
                    throw Conflicto(ReglasReposicionPuntual.TextoRecogida(viajeAhora.FechaRecogida.Value));
                }
                await ComprobarQueNoFaltaParaSusPedidos(empresa, origen, lineas).ConfigureAwait(false);
                numeroTraspaso = await repositorio.EnTransaccion(async () =>
                {
                    foreach (LineaCrearReposicionDTO linea in lineas)
                    {
                        int insertadas = await repositorio.InsertarLineaPreparacion(empresa, diario, origen, destino, linea.Producto, linea.Cantidad,
                            momento.Date, texto, VENDEDOR_VENTA, quien).ConfigureAwait(false);
                        if (insertadas != 1)
                        {
                            throw new NestoBusinessException($"El producto {linea.Producto} no existe. No se ha mandado nada.");
                        }
                    }
                    creadas = await repositorio.LeerLineasEnPreparacion(empresa, diario, origen).ConfigureAwait(false);
                    List<FilaReposicionEnPreparacion> sinStock = creadas.Where(f => f.Cantidad > f.StockOrigen).ToList();
                    if (sinStock.Any())
                    {
                        throw new NestoBusinessException($"No hay stock suficiente en {origen}: " +
                            string.Join(", ", sinStock.Select(f => $"{f.Producto} (pides {f.Cantidad}, hay {Math.Max(0, f.StockOrigen)})")) +
                            ". No se ha mandado nada.");
                    }
                    int numero = await CerrarPreparacion(empresa, origen, destino, diario, almacenDestino, creadas, huecos, quien,
                        contabilizar: true).ConfigureAwait(false);
                    await registro.Crear(new CabeceraReposicionTraspaso
                    {
                        Empresa = empresa,
                        NumTraspaso = numero,
                        Origen = origen,
                        Destino = destino,
                        Herramienta = HerramientasReposicion.PUNTUAL,
                        UsuarioCreacion = quien,
                        FechaCreacion = momento,
                        FechaCorte = null
                    }).ConfigureAwait(false);
                    await registro.MarcarPreparada(empresa, numero, quien).ConfigureAwait(false);
                    return numero;
                }).ConfigureAwait(false);
            }

            return new ResultadoTerminarReposicionDTO
            {
                NumTraspaso = numeroTraspaso,
                Origen = origen,
                Destino = destino,
                DiarioSalida = diario,
                DiarioEntrada = almacenDestino.DiarioEntradaRep,
                Lineas = creadas.Select(f => new LineaTraspasoTerminadoDTO { Producto = f.Producto, Nombre = f.Nombre, Cantidad = f.Cantidad }).ToList()
            };
        }

        public async Task<EstadoReposicionPuntualDTO> LeerEstadoPuntual(string empresa, string origen)
        {
            empresa = Empresa(empresa);
            origen = Almacen(origen);
            string destino = Constantes.Almacenes.ALGETE;
            ComprobarAlmacenes(origen, destino);
            DatosAlmacenReposicion almacenOrigen = await AlmacenConDiario(empresa, origen, a => a.DiarioSalidaRep, "de salida").ConfigureAwait(false);
            bool enPreparacion = (await repositorio.LeerLineasEnPreparacion(empresa, almacenOrigen.DiarioSalidaRep, origen).ConfigureAwait(false)).Any();
            return await CalcularEstadoPuntual(empresa, origen, destino, ahora(), enPreparacion).ConfigureAwait(false);
        }

        /// <summary>
        /// Sugerencia 581 (Carlos, 13/10/26: «no pasa todos los días a la misma hora, no sé fijarlo salvo que el usuario informe
        /// cuando pasa»): la tienda (o, más adelante, el repartidor desde Ariadna) marca que el repartidor ya se ha llevado la
        /// reposición de hoy. Se apunta quién y cuándo en ReposicionesTraspasos (FechaRecogida, UsuarioRecogida) del viaje de hoy
        /// (las cabeceras del job con el corte de hoy) y en las puntuales que ya se hayan mandado hoy. Desde entonces la puntual da
        /// 409: lo que quede va en la siguiente. Con el mismo applock que la puntual y el job (origen + día).
        /// </summary>
        public async Task<EstadoReposicionPuntualDTO> MarcarRecogida(MarcarRecogidaReposicionDTO peticion, IPrincipal usuario)
        {
            if (peticion == null)
            {
                throw new NestoBusinessException("Falta la reposición de la que se marca la recogida.");
            }
            string empresa = Empresa(peticion.Empresa);
            string origen = Almacen(peticion.Origen);
            string destino = string.IsNullOrWhiteSpace(peticion.Destino) ? Constantes.Almacenes.ALGETE : Almacen(peticion.Destino);
            ComprobarAlmacenes(origen, destino);
            if (destino != Constantes.Almacenes.ALGETE)
            {
                throw new NestoBusinessException("De momento la recogida solo se marca en las reposiciones de una tienda a Algete.");
            }
            DatosAlmacenReposicion almacenOrigen = await AlmacenConDiario(empresa, origen, a => a.DiarioSalidaRep, "de salida").ConfigureAwait(false);
            if (almacenOrigen.ControlUbicaciones)
            {
                throw new NestoBusinessException($"De momento la recogida solo se marca en las reposiciones de una tienda a Algete: desde {origen} no.");
            }
            if (!PuedeEscribir(usuario, empresa, origen))
            {
                throw new UnauthorizedAccessException($"La recogida de la reposición de {origen} solo la puede marcar la gente de esa tienda (o Almacén y Dirección). " +
                    "Tu usuario no tiene ese almacén como almacén de pedidos.");
            }
            DateTime momento = ahora();
            string quien = UsuarioAuditoriaHelper.Resolver(usuario, null);
            using (await bloquear(empresa, origen, momento.Date).ConfigureAwait(false))
            {
                bool enPreparacion = (await repositorio.LeerLineasEnPreparacion(empresa, almacenOrigen.DiarioSalidaRep, origen).ConfigureAwait(false)).Any();
                EstadoReposicionPuntualDTO estado = await CalcularEstadoPuntual(empresa, origen, destino, momento, enPreparacion).ConfigureAwait(false);
                if (estado.Situacion != SituacionesReposicionPuntual.ABIERTA)
                {
                    throw Conflicto(estado.Motivo);
                }
                if (!estado.PuedeMarcarRecogida)
                {
                    throw Conflicto("Todavía no se puede marcar la recogida: falta lanzar Scripts/Issue581_ReposicionesTraspasos_Recogida.sql en la base de datos.");
                }
                await viajes.MarcarRecogida(empresa, origen, destino, estado.CierreHoy.Value.Date, momento, quien).ConfigureAwait(false);
                return await CalcularEstadoPuntual(empresa, origen, destino, momento, enPreparacion).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sugerencia 581: el viaje de HOY de la ruta (el que llega hoy, laborable en los dos almacenes, como el job), si el job ya
        /// lo ha rellenado y si el repartidor ya se lo ha llevado. Ver <see cref="ReglasReposicionPuntual"/>.
        /// </summary>
        private async Task<EstadoReposicionPuntualDTO> CalcularEstadoPuntual(string empresa, string origen, string destino, DateTime momento, bool enPreparacion)
        {
            List<ReposicionCalendario> deLaRuta = ((await viajes.LeerCalendario(empresa).ConfigureAwait(false)) ?? new List<ReposicionCalendario>())
                .Where(f => f != null && f.Activo
                    && string.Equals(f.Empresa?.Trim(), empresa, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(f.AlmacenOrigen?.Trim(), origen, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(f.AlmacenDestino?.Trim(), destino, StringComparison.OrdinalIgnoreCase))
                .ToList();
            CorteReposicion viajeHoy = calculadora.CortesQueLleganElDia(deLaRuta, momento).FirstOrDefault();
            ViajeReposicionDelDia viaje = null;
            string nombreOrigen = null;
            string nombreDestino = null;
            bool festivo = false;
            if (viajeHoy != null)
            {
                viaje = await viajes.LeerViajeDelDia(empresa, origen, destino, viajeHoy.Corte.Date).ConfigureAwait(false);
            }
            else
            {
                // Hay fila para hoy (lunes a viernes) pero no viaje: festivo en la tienda o en Algete
                festivo = momento.DayOfWeek != DayOfWeek.Saturday && momento.DayOfWeek != DayOfWeek.Sunday
                    && deLaRuta.Any(f => f.DiaSemana == CalculadoraFechaReposicion.DiaSemana(momento));
                nombreOrigen = await repositorio.LeerNombreAlmacen(empresa, origen).ConfigureAwait(false);
                nombreDestino = await repositorio.LeerNombreAlmacen(empresa, destino).ConfigureAwait(false);
            }
            return ReglasReposicionPuntual.Calcular(origen, destino, nombreOrigen, nombreDestino, viajeHoy, festivo, viaje, enPreparacion, momento);
        }

        /// <summary>
        /// Sugerencia 581 (Carlos, 13/10/26): la puntual no se lleva a Algete lo que necesitan los pedidos pendientes de la propia
        /// tienda (<see cref="ProductoReposicionPuntualDTO.Disponible"/>). 400 con cada producto: «pides X, puedes mandar Y».
        /// </summary>
        private async Task ComprobarQueNoFaltaParaSusPedidos(string empresa, string origen, List<LineaCrearReposicionDTO> lineas)
        {
            Dictionary<string, ProductoReposicionPuntualDTO> libres = await stockLibre.Leer(empresa, origen, lineas.Select(l => l.Producto)).ConfigureAwait(false)
                ?? new Dictionary<string, ProductoReposicionPuntualDTO>();
            var demasiado = new List<string>();
            foreach (LineaCrearReposicionDTO linea in lineas)
            {
                if (libres.TryGetValue(linea.Producto, out ProductoReposicionPuntualDTO libre) && linea.Cantidad > libre.Disponible)
                {
                    demasiado.Add($"{linea.Producto} (pides {linea.Cantidad}, puedes mandar {Math.Max(0, libre.Disponible)})");
                }
            }
            if (demasiado.Any())
            {
                throw new NestoBusinessException($"{origen} no puede mandar tanto: lo necesitan sus propios pedidos pendientes. " +
                    string.Join(", ", demasiado) + ". No se ha mandado nada.");
            }
        }

        private sealed class SinBloqueo : IDisposable
        {
            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Quién puede crear, cambiar y terminar la reposición de un origen: quien tiene ese almacén en AlmacénPedidoVta (la
        /// tienda) y, además, Almacén y Dirección. El mismo criterio que para recibirla (OrigenRecepcionReposiciones). Desde
        /// un origen con control de ubicaciones (Algete), solo Almacén y Dirección: es quien la recoge en Ariadna
        /// (OrigenSalidaReposicion.PuedeTerminar).
        /// </summary>
        public bool PuedeEscribir(IPrincipal usuario, string empresa, string origen, bool controlUbicaciones = false)
        {
            if (usuario?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(usuario.Identity.Name))
            {
                return false;
            }
            if (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN) || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION))
            {
                return true;
            }
            if (controlUbicaciones)
            {
                return false;
            }
            string nombre = usuario.Identity.Name;
            string sinDominio = nombre.Contains("\\") ? nombre.Substring(nombre.LastIndexOf('\\') + 1) : nombre;
            string suyo = almacenDelUsuario(empresa, sinDominio.Trim());
            return !string.IsNullOrWhiteSpace(suyo) && string.Equals(suyo.Trim(), origen, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            dbPropio?.Dispose();
        }

        // ---- privados ----

        /// <summary>
        /// Cerrar la preparación, los pasos de «Contabilizar» de Nesto viejo en su orden. Con <paramref name="contabilizar"/>
        /// (tiendas) termina con prdExtrProducto del diario de salida; sin él (Algete) la salida queda en PreExtrProducto
        /// «por salir» y la contabiliza Ariadna al terminar la recogida. Va dentro de la transacción del llamante.
        /// </summary>
        private async Task<int> CerrarPreparacion(string empresa, string origen, string destino, string diario, DatosAlmacenReposicion almacenDestino,
            List<FilaReposicionEnPreparacion> conCantidad, IUbicacionesReposicion huecos, string quien, bool contabilizar)
        {
            // (2) Nesto viejo: select empresa from vstreposiciónAlmacénAbajo where diario=… and almacén<>'ALG' and estado>=0 → tiene que ser 0
            int deOtroAlmacen = await repositorio.ContarLineasDeOtroAlmacen(empresa, diario, destino, soloSinTraspaso: !contabilizar).ConfigureAwait(false);
            if (deOtroAlmacen > 0)
            {
                throw Conflicto($"En el diario {diario} de {origen} hay {deOtroAlmacen} líneas que no son de la reposición a {destino}: " +
                    "hay que contabilizarlas o quitarlas desde Nesto viejo antes de terminar.");
            }
            // (3) select traspasoalmacén from contadoresglobales → +1; update contadoresglobales set traspasoalmacén=80893
            int numero = await repositorio.NuevoNumeroTraspaso().ConfigureAwait(false);
            DateTime momento = ahora();
            // (4) delete … and cantidad=0
            _ = await repositorio.BorrarLineasACero(empresa, diario, destino).ConfigureAwait(false);
            // (5) update … set nºtraspaso=80893, fecha=ahora
            int numeradas = await repositorio.AsignarTraspaso(empresa, diario, destino, numero, momento).ConfigureAwait(false);
            if (numeradas != conCantidad.Count)
            {
                throw Conflicto($"La reposición de {origen} ha cambiado mientras tanto ({numeradas} líneas numeradas, {conCantidad.Count} esperadas): " +
                    "vuelve a cargarla.");
            }
            // (6) update pedidosespeciales set NºTraspaso=… (líneas «Compra», sin vendedor)
            _ = await repositorio.ActualizarPedidosEspeciales(empresa, diario, destino).ConfigureAwait(false);
            // (7) Con control de ubicaciones. Si se contabiliza ya: update ubicaciones set cantidad=-cantidad, estado=-4,
            // nºtraspasorepo=… como Nesto viejo. NestoAPI#651: si NO (Algete, la contabiliza Ariadna al terminar la recogida),
            // las reservas siguen vivas en 4 y solo se enlazan al traspaso; pasar a -4 aquí dejaba las Ubicaciones vivas N por
            // debajo del stock del extracto hasta que se terminaba la recogida.
            List<int> lineasEntrada = conCantidad.Select(f => f.NumeroOrden).ToList();
            _ = contabilizar
                ? await huecos.DescontarAlTerminar(empresa, lineasEntrada, numero, quien).ConfigureAwait(false)
                : await huecos.EnlazarAlTraspaso(empresa, lineasEntrada, numero, quien).ConfigureAwait(false);
            // (8) la SALIDA: las mismas líneas con Almacén = origen y cantidad negativa
            int salidas = await repositorio.InsertarSalida(empresa, diario, origen, destino, momento, quien).ConfigureAwait(false);
            if (salidas != numeradas)
            {
                throw new NestoBusinessException($"Al crear la salida de {origen} se han insertado {salidas} líneas y había {numeradas}. No se ha hecho nada.");
            }
            // (9) la ENTRADA al diario de entrada del destino (sigue con Estado 3 y NºTraspaso: pendiente de recibir)
            int movidas = await repositorio.MoverEntradaADiario(empresa, diario, destino, almacenDestino.DiarioEntradaRep).ConfigureAwait(false);
            if (movidas != numeradas)
            {
                throw new NestoBusinessException($"Al pasar la entrada al diario {almacenDestino.DiarioEntradaRep} se han movido {movidas} líneas y había {numeradas}. No se ha hecho nada.");
            }
            if (contabilizar)
            {
                // (10) Nesto viejo llama a prdExtrProducto('1','RepoAlcAlg'): la salida pasa al extracto
                await repositorio.Contabilizar(empresa, diario, quien).ConfigureAwait(false);
                // (11) update preextrproducto set estado=1 where número<>'R ' and diario=… (0 filas; por fidelidad)
                _ = await repositorio.MarcarRestoEstado1(empresa, diario).ConfigureAwait(false);
            }
            return numero;
        }

        private void ComprobarPuedeEscribir(IPrincipal usuario, string empresa, string origen, bool controlUbicaciones)
        {
            if (!PuedeEscribir(usuario, empresa, origen, controlUbicaciones))
            {
                throw new UnauthorizedAccessException(controlUbicaciones
                    ? $"Las reposiciones desde {origen} las preparan Almacén y Dirección."
                    : $"Las reposiciones de {origen} las prepara la gente de ese almacén (o Almacén y Dirección). " +
                      "Tu usuario no tiene ese almacén como almacén de pedidos.");
            }
        }

        private static void ComprobarAlmacenes(string origen, string destino)
        {
            if (!ALMACENES_REPOSICION.Contains(origen) || !ALMACENES_REPOSICION.Contains(destino))
            {
                throw new NestoBusinessException($"Los almacenes de una reposición tienen que ser {string.Join(", ", ALMACENES_REPOSICION)}.");
            }
            if (origen == destino)
            {
                throw new NestoBusinessException("El almacén de origen y el de destino no pueden ser el mismo.");
            }
        }

        private async Task<DatosAlmacenReposicion> AlmacenConDiario(string empresa, string almacen, Func<DatosAlmacenReposicion, string> diario, string cual)
        {
            DatosAlmacenReposicion datos = await repositorio.LeerAlmacen(empresa, almacen).ConfigureAwait(false);
            if (datos == null)
            {
                throw new NestoBusinessException($"El almacén {almacen} no existe en la empresa {empresa}.");
            }
            if (string.IsNullOrWhiteSpace(diario(datos)))
            {
                throw new NestoBusinessException($"El almacén {almacen} no tiene diario {cual} de reposiciones (Almacenes): no puede hacer reposiciones.");
            }
            return datos;
        }

        private async Task<List<LineaCrearReposicionDTO>> LineasACrear(CrearReposicionDTO peticion, string empresa, string origen, string destino,
            DateTime? corte)
        {
            IEnumerable<LineaCrearReposicionDTO> origenLineas;
            if (peticion.Lineas != null && peticion.Lineas.Any())
            {
                origenLineas = peticion.Lineas;
            }
            else
            {
                // El botón «Rellenar» de Nesto viejo: lo que dice prdRellenarReposicionStock2 (solo «Venta»; las de «Compra» no salen hoy)
                List<LineaPropuestaReposicionDTO> calculada = await propuesta(empresa, origen, destino, corte).ConfigureAwait(false);
                origenLineas = calculada.Select(l => new LineaCrearReposicionDTO { Producto = l.Producto, Cantidad = l.CantidadReposicion });
            }
            return Consolidar(origenLineas);
        }

        /// <summary>Nesto viejo consolida por producto (group by producto, grupo … sum(cantidad)); fuera lo que no tiene cantidad.</summary>
        private static List<LineaCrearReposicionDTO> Consolidar(IEnumerable<LineaCrearReposicionDTO> origenLineas)
        {
            return (origenLineas ?? Enumerable.Empty<LineaCrearReposicionDTO>())
                .Where(l => !string.IsNullOrWhiteSpace(l.Producto) && l.Cantidad > 0)
                .GroupBy(l => l.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new LineaCrearReposicionDTO { Producto = g.Key, Cantidad = g.Sum(l => l.Cantidad) })
                .OrderBy(l => l.Producto, StringComparer.Ordinal)
                .ToList();
        }

        private async Task<ReposicionEnPreparacionDTO> LeerEnPreparacion(string empresa, string origen, DatosAlmacenReposicion almacen)
        {
            List<FilaReposicionEnPreparacion> filas = await repositorio.LeerLineasEnPreparacion(empresa, almacen.DiarioSalidaRep, origen).ConfigureAwait(false);
            return filas.Any() ? await ConNombres(Dto(empresa, origen, almacen.DiarioSalidaRep, filas, null)).ConfigureAwait(false) : null;
        }

        /// <summary>NestoAPI#600: los nombres de origen y destino y el título («De Algete a Alcobendas»), resueltos aquí.</summary>
        private async Task<ReposicionEnPreparacionDTO> ConNombres(ReposicionEnPreparacionDTO reposicion)
        {
            reposicion.NombreOrigen = await repositorio.LeerNombreAlmacen(reposicion.Empresa, reposicion.Origen).ConfigureAwait(false);
            reposicion.NombreDestino = await repositorio.LeerNombreAlmacen(reposicion.Empresa, reposicion.Destino).ConfigureAwait(false);
            reposicion.Titulo = ServicioPropuestaReposicion.TituloReposicion(reposicion.Origen, reposicion.NombreOrigen,
                reposicion.Destino, reposicion.NombreDestino);
            return reposicion;
        }

        private static ReposicionEnPreparacionDTO Dto(string empresa, string origen, string diario, List<FilaReposicionEnPreparacion> filas, ResumenUbicaciones reservas)
        {
            Dictionary<int, ReservaLineaReposicion> porLinea = (reservas?.Reservas ?? new List<ReservaLineaReposicion>())
                .GroupBy(r => r.NumeroOrdenEntrada).ToDictionary(g => g.Key, g => g.First());
            return new ReposicionEnPreparacionDTO
            {
                Empresa = empresa,
                Origen = origen,
                Destino = filas.First().Almacen?.Trim(),
                Diario = diario,
                Fecha = filas.Min(f => f.Fecha),
                Usuario = filas.First().Usuario,
                Lineas = filas.Select(f =>
                {
                    var linea = new LineaReposicionEnPreparacionDTO
                    {
                        NumeroOrden = f.NumeroOrden,
                        Producto = f.Producto,
                        Nombre = f.Nombre,
                        // NestoAPI#674: un código que no es GTIN válido no se manda (Ariadna lo trata como sin código)
                        CodigoBarras = Productos.ValidadorGtin.SoloSiValido(f.CodigoBarras),
                        Cantidad = f.Cantidad,
                        StockOrigen = f.StockOrigen
                    };
                    if (reservas != null)
                    {
                        porLinea.TryGetValue(f.NumeroOrden, out ReservaLineaReposicion reserva);
                        linea.Hueco = UbicacionesReposicion.Huecos(reserva);
                        HuecoUbicacion primero = reserva?.Piezas.Select(p => p.Hueco).FirstOrDefault(h => h?.TieneHueco == true);
                        linea.HuecoDeReferencia = primero == null ? null : Models.PreparacionAlmacen.UbicacionAlmacenDTO.De(primero.Pasillo, primero.Fila, primero.Columna);
                        linea.UnidadesSinHueco = reserva?.UnidadesSinHueco ?? f.Cantidad;
                        linea.SinHueco = linea.UnidadesSinHueco > 0;
                    }
                    return linea;
                }).ToList()
            };
        }

        /// <summary>Nesto viejo: «Traspaso por reposición de almacén ALC a ALG» (cabe en los 50 de PreExtrProducto.Texto).</summary>
        internal static string Texto(string origen, string destino)
        {
            return $"Traspaso por reposición de almacén {origen} a {destino}";
        }

        private static NestoBusinessException Conflicto(string mensaje)
        {
            return new NestoBusinessException(mensaje) { StatusCode = HttpStatusCode.Conflict };
        }

        private static string Empresa(string empresa)
        {
            return string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
        }

        private static string Almacen(string almacen)
        {
            return almacen?.Trim().ToUpperInvariant() ?? string.Empty;
        }
    }
}
