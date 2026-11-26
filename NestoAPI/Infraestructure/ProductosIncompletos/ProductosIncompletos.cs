using NestoAPI.Infraestructure.AlmacenesFicticios;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.ProductosIncompletos
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#902 (OK de Carlos 24/11/26): «Sacar una pieza de un producto» sin que sea un inventario. Caso de origen: el cable
    // 43415 (portaagujas Depiblend) se daba de alta por inventario cada vez que había que servir uno, sacándolo de un Depiblend
    // nuevo (40252), que quedaba incompleto pero seguía contando como disponible. Estos productos NO son kits ni se montan:
    // no se usa la tabla Kits ni hay lista de piezas; la API solo RECUERDA qué piezas se han sacado de cada producto.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>dbo.ProductosIncompletos.Estado (CHECK en Scripts/Issue902_ProductosIncompletos.sql).</summary>
    public static class EstadosProductoIncompleto
    {
        public const string INCOMPLETO = "Incompleto";
        public const string COMPLETADO = "Completado";
        public const string BAJA = "Baja";
    }

    /// <summary>POST api/Almacen/ProductosIncompletos/SacarPieza.</summary>
    public class SacarPiezaDTO
    {
        /// <summary>Donde está la unidad donante y donde entra la pieza: ALG, REI o ALC.</summary>
        public string Almacen { get; set; }
        public string ProductoDonante { get; set; }
        /// <summary>Algete: el hueco de donde sale la unidad donante (si no viene, lo propone la API).</summary>
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        /// <summary>Obligatorio si el donante tiene Productos.NecesitaNumSerie.</summary>
        public string NumeroSerie { get; set; }
        public string Pieza { get; set; }
        public int Cantidad { get; set; } = 1;
        /// <summary>El pedido para el que se saca la pieza (opcional).</summary>
        public int? Pedido { get; set; }
        public string Comentario { get; set; }
        /// <summary>Opcional: dónde se deja la unidad incompleta (hueco de la zona de incompletos de Algete, «002002004» o «002/002/004»).</summary>
        public string HuecoDestino { get; set; }
        /// <summary>Idempotencia: con la misma clave se devuelve lo de la primera vez sin repetir nada.</summary>
        public Guid? Clave { get; set; }
    }

    /// <summary>POST api/Almacen/ProductosIncompletos/{id}/Completar.</summary>
    public class CompletarProductoIncompletoDTO
    {
        /// <summary>Algete: el hueco de donde sale la pieza que la completa (si no viene: lo pendiente de ubicar y, si no, el único hueco).</summary>
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public string Comentario { get; set; }
    }

    /// <summary>POST api/Almacen/ProductosIncompletos/{id}/Baja.</summary>
    public class BajaProductoIncompletoDTO
    {
        public string Motivo { get; set; }
    }

    /// <summary>Una ficha de dbo.ProductosIncompletos, con los nombres y el valor a PVP.</summary>
    public class ProductoIncompletoDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        /// <summary>El almacén de donde salió la unidad y adonde vuelve (y donde entró la pieza).</summary>
        public string Almacen { get; set; }
        /// <summary>El almacén ficticio de incompletos donde está la unidad mientras está incompleta.</summary>
        public string AlmacenIncompletos { get; set; }
        public string ProductoDonante { get; set; }
        public string NombreDonante { get; set; }
        public string NumeroSerie { get; set; }
        public string Pieza { get; set; }
        public string NombrePieza { get; set; }
        /// <summary>Unidades de la pieza que faltan.</summary>
        public int Cantidad { get; set; }
        public int? Pedido { get; set; }
        public string Comentario { get; set; }
        /// <summary>Algete: el hueco de donde salió la unidad («002/002/004»).</summary>
        public string HuecoOrigen { get; set; }
        /// <summary>Dónde se ha dejado la unidad incompleta («002/002/004»), si se dijo.</summary>
        public string HuecoDestino { get; set; }
        public string Estado { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        public int? Traspaso { get; set; }
        public DateTime? FechaCierre { get; set; }
        public string UsuarioCierre { get; set; }
        public string MotivoCierre { get; set; }
        public int? TraspasoCierre { get; set; }
        /// <summary>PVP de la ficha del donante (empresa 1).</summary>
        public decimal? PvpDonante { get; set; }
        /// <summary>Valor inmovilizado a PVP: una unidad del donante (null si no tiene PVP).</summary>
        public decimal? ValorPvp => PvpDonante;
        /// <summary>Días desde que se sacó la pieza (hasta hoy si sigue incompleto; hasta el cierre si no).</summary>
        public int Dias { get; set; }
        /// <summary>«En INC, hueco 002/002/004 (salió de ALG)».</summary>
        public string Donde { get; set; }
        /// <summary>Lo que se lee en la etiqueta: «INC{Id}».</summary>
        public string CodigoEtiqueta => Id > 0 ? ReglasProductosIncompletos.PREFIJO_ETIQUETA + Id.ToString(CultureInfo.InvariantCulture) : null;
        public bool EstaViva => string.Equals(Estado, EstadosProductoIncompleto.INCOMPLETO, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Un apunte de stock (PreExtrProducto → ExtractoProducto por prdExtrProducto).</summary>
    public class ApunteProductoIncompletoDTO
    {
        public string Diario { get; set; }
        public string Almacen { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public string Texto { get; set; }
    }

    /// <summary>La etiqueta «INCOMPLETO»: qué lleva, dónde se ha mandado y si ha salido.</summary>
    public class EtiquetaIncompletoDTO
    {
        public List<string> Lineas { get; set; } = new List<string>();
        public string Codigo { get; set; }
        public string Impresora { get; set; }
        public bool Impresa { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>Lo que devuelven Sacar pieza, Completar y Baja (también en ensayo).</summary>
    public class ResultadoProductoIncompletoDTO
    {
        public bool Ensayo { get; set; }
        public string Operacion { get; set; }
        public ProductoIncompletoDTO Ficha { get; set; }
        /// <summary>Las demás fichas de la misma unidad que se cierran a la vez (Baja).</summary>
        public List<int> FichasCerradas { get; set; } = new List<int>();
        public int? NumeroTraspaso { get; set; }
        /// <summary>Si la unidad se ha movido entre su almacén y el de incompletos (no, si la unidad ya estaba apartada por otra ficha o sigue incompleta).</summary>
        public bool UnidadMovida { get; set; }
        public List<ApunteProductoIncompletoDTO> Apuntes { get; set; } = new List<ApunteProductoIncompletoDTO>();
        /// <summary>Algete: el hueco de donde sale la unidad donante (Sacar) o la pieza (Completar).</summary>
        public string Hueco { get; set; }
        public bool HuecoPropuesto { get; set; }
        /// <summary>Algete: unidades que quedan pendientes de ubicar (la pieza al sacarla; la unidad al completarla).</summary>
        public int UnidadesPendientesDeUbicar { get; set; }
        public EtiquetaIncompletoDTO Etiqueta { get; set; }
        public bool AvisoCompras { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>GET api/Almacen/ProductosIncompletos.</summary>
    public class ListadoProductosIncompletosDTO
    {
        public List<ProductoIncompletoDTO> Fichas { get; set; } = new List<ProductoIncompletoDTO>();
        public int Total => Fichas?.Count ?? 0;
        /// <summary>Unidades incompletas distintas entre las vivas (dos piezas de la misma serie = una unidad).</summary>
        public int UnidadesIncompletas { get; set; }
        /// <summary>Valor inmovilizado a PVP de las unidades incompletas vivas.</summary>
        public decimal ValorPvp { get; set; }
    }

    /// <summary>GET api/Almacen/ProductosIncompletos/PiezasSacadas: lo que ya se ha sacado alguna vez de ese producto (para proponer).</summary>
    public class PiezaSacadaDTO
    {
        public string Pieza { get; set; }
        public string Nombre { get; set; }
        public int Veces { get; set; }
        public int Unidades { get; set; }
        public DateTime UltimaVez { get; set; }
    }

    /// <summary>Lo que se lee de Productos para estas operaciones.</summary>
    public class ProductoIncompletoLeido
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Estado { get; set; }
        public bool Ficticio { get; set; }
        public bool Ubicar { get; set; }
        public bool NecesitaNumSerie { get; set; }
        public decimal? Pvp { get; set; }
    }

    /// <summary>Quién pide y qué puede (para LA REGLA de permisos).</summary>
    public class ContextoUsuarioIncompletos
    {
        public string Usuario { get; set; }
        public bool EsDireccion { get; set; }
        public bool EsInformatica { get; set; }
        public bool EsAlmacen { get; set; }
        public bool EsTiendas { get; set; }
        public bool EsCompras { get; set; }
        /// <summary>Tiendas: su almacén (ParametrosUsuario «AlmacénPedidoVta»: REI o ALC).</summary>
        public string AlmacenTienda { get; set; }
    }

    // ---------------------------------------------------------------- LAS REGLAS (puras)

    /// <summary>NestoAPI#902: validaciones, permisos, qué se mueve y los textos. Pura: sin base de datos.</summary>
    public static class ReglasProductosIncompletos
    {
        /// <summary>ParametrosUsuario empresa 1, fila «(defecto)»: el código del almacén ficticio de incompletos.</summary>
        public const string CLAVE_PARAMETRO = "AlmacenIncompletos";
        /// <summary>Diario de sistema de sacar y completar (DiariosProducto; nunca _RegInvent: no es un inventario).</summary>
        public const string DIARIO = "_SacaPieza";
        /// <summary>Diario de sistema de la baja de una unidad incompleta (merma con causa conocida).</summary>
        public const string DIARIO_BAJA = "_BajaIncom";
        public const string TABLA = "ProductosIncompletos";
        public const string TABLA_MODIFICACIONES = "ProductoIncompleto";
        public const string TIPO_NOTIFICACION = "ProductoIncompleto";
        public const string PREFIJO_ETIQUETA = "INC";
        public const int LONGITUD_SERIE = 50;
        public const int LONGITUD_COMENTARIO = 500;
        public const int MAXIMO_CANTIDAD = 999;

        public const string MENSAJE_FALTA_SCRIPT = "Los productos incompletos aún no están disponibles: falta lanzar Scripts/Issue902_ProductosIncompletos.sql.";
        public const string MENSAJE_SIN_PERMISO = "No tiene permiso para sacar piezas de productos: es cosa de Almacén, Dirección, Informática y de cada tienda con lo suyo.";
        public const string MENSAJE_SIN_PERMISO_CONSULTAR = "No tiene permiso para ver los productos incompletos.";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        public static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        public static string Codigo(string texto) => Limpio(texto)?.ToUpperInvariant();

        private static bool Igual(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        public static bool EsSede(string almacen) => Constantes.Sedes.ListaSedes.Any(s => Igual(s, almacen));

        // ---- Permisos

        /// <summary>Ver el listado y las piezas sacadas: Almacén, Compras, Dirección, Informática y Tiendas (las de su almacén).</summary>
        public static bool PuedeConsultar(ContextoUsuarioIncompletos usuario) =>
            usuario != null && (usuario.EsDireccion || usuario.EsInformatica || usuario.EsAlmacen || usuario.EsCompras || usuario.EsTiendas);

        /// <summary>
        /// LA REGLA de quién saca, completa y da de baja (Carlos 24/11/26): Almacén, Dirección e Informática en cualquier sede; Tiendas
        /// solo en la suya (AlmacénPedidoVta). Null si puede; si no, el motivo (403).
        /// </summary>
        public static string MotivoNoPuedeOperar(ContextoUsuarioIncompletos usuario, string almacen)
        {
            if (usuario == null)
            {
                return MENSAJE_SIN_PERMISO;
            }
            if (usuario.EsDireccion || usuario.EsInformatica || usuario.EsAlmacen)
            {
                return null;
            }
            if (usuario.EsTiendas)
            {
                if (string.IsNullOrWhiteSpace(usuario.AlmacenTienda))
                {
                    return "No tiene almacén de tienda (parámetro AlmacénPedidoVta): pídaselo a Informática.";
                }
                return Igual(usuario.AlmacenTienda, almacen)
                    ? null
                    : $"Desde la tienda solo puede hacerlo con lo de {usuario.AlmacenTienda.Trim().ToUpperInvariant()}, no con lo de {Codigo(almacen)}.";
            }
            return MENSAJE_SIN_PERMISO;
        }

        /// <summary>Puede hacer alguna de las operaciones en algún almacén (para dar 403 antes de mirar nada más).</summary>
        public static bool PuedeOperarAlguno(ContextoUsuarioIncompletos usuario) =>
            usuario != null && (usuario.EsDireccion || usuario.EsInformatica || usuario.EsAlmacen || usuario.EsTiendas);

        /// <summary>Las tiendas ven solo lo suyo en el listado.</summary>
        public static string AlmacenVisible(ContextoUsuarioIncompletos usuario, string pedido)
        {
            bool todo = usuario != null && (usuario.EsDireccion || usuario.EsInformatica || usuario.EsAlmacen || usuario.EsCompras);
            return todo ? Codigo(pedido) : Codigo(usuario?.AlmacenTienda) ?? "---";
        }

        // ---- El almacén de incompletos

        /// <summary>
        /// El almacén de incompletos sirve si está en el parámetro, existe y está activo, NO es una sede (ALG, REI, ALC: lo que cuentan
        /// el stock disponible, el motor de fechas y la tienda) y no lleva control de ubicaciones. Null si sirve; si no, el motivo (503).
        /// </summary>
        public static string MotivoAlmacenIncompletosNoValido(string codigo, IEnumerable<AlmacenLeido> almacenes)
        {
            string limpio = Codigo(codigo);
            if (limpio == null)
            {
                return $"Falta el almacén de incompletos: póngalo en ParametrosUsuario (empresa 1, «(defecto)», {CLAVE_PARAMETRO}).";
            }
            if (EsSede(limpio))
            {
                return $"El almacén de incompletos no puede ser {limpio}: esas unidades contarían como stock disponible.";
            }
            AlmacenLeido almacen = (almacenes ?? Enumerable.Empty<AlmacenLeido>()).FirstOrDefault(a => Igual(a?.Codigo, limpio));
            if (almacen == null)
            {
                return $"El almacén de incompletos {limpio} no existe o no está activo.";
            }
            if (almacen.ControlUbicaciones)
            {
                return $"El almacén de incompletos {limpio} no puede llevar control de ubicaciones.";
            }
            // Carlos 26/11/26: el de incompletos es FICTICIO («INC»): sus unidades no son stock vendible para nadie (tampoco Nesto viejo)
            if (!almacen.EsFicticio)
            {
                return $"El almacén de incompletos {limpio} tiene que ser un almacén ficticio (AlmacénFicticio = 1).";
            }
            return null;
        }

        // ---- Validaciones de la petición

        public static string MotivoPeticionNoValida(SacarPiezaDTO peticion)
        {
            if (peticion == null)
            {
                return "Falta la petición.";
            }
            if (Limpio(peticion.Almacen) == null)
            {
                return "Diga de qué almacén es la unidad (ALG, REI o ALC).";
            }
            if (!EsSede(peticion.Almacen))
            {
                return $"{Codigo(peticion.Almacen)} no es un almacén donde se pueda sacar una pieza: solo ALG, REI o ALC.";
            }
            if (Limpio(peticion.ProductoDonante) == null)
            {
                return "Diga de qué producto se saca la pieza.";
            }
            if (Limpio(peticion.Pieza) == null)
            {
                return "Diga qué pieza se saca.";
            }
            if (Igual(peticion.Pieza, peticion.ProductoDonante))
            {
                return "La pieza no puede ser el mismo producto del que se saca.";
            }
            if (peticion.Cantidad < 1 || peticion.Cantidad > MAXIMO_CANTIDAD)
            {
                return $"La cantidad de la pieza tiene que estar entre 1 y {MAXIMO_CANTIDAD}.";
            }
            if (peticion.Pedido.HasValue && peticion.Pedido.Value <= 0)
            {
                return "El número de pedido no es válido.";
            }
            if ((Limpio(peticion.NumeroSerie)?.Length ?? 0) > LONGITUD_SERIE)
            {
                return $"El número de serie no puede pasar de {LONGITUD_SERIE} caracteres.";
            }
            if ((Limpio(peticion.Comentario)?.Length ?? 0) > LONGITUD_COMENTARIO)
            {
                return $"El comentario no puede pasar de {LONGITUD_COMENTARIO} caracteres.";
            }
            string hueco = MotivoHuecoNoValido(peticion.Pasillo, peticion.Fila, peticion.Columna);
            if (hueco != null)
            {
                return hueco;
            }
            if (Limpio(peticion.HuecoDestino) != null && HuecoUbicacion.DeCodigo(peticion.HuecoDestino) == null)
            {
                return $"El hueco de destino «{peticion.HuecoDestino.Trim()}» no es válido: tienen que ser 9 cifras (pasillo, fila y columna).";
            }
            return null;
        }

        /// <summary>O los tres o ninguno.</summary>
        public static string MotivoHuecoNoValido(string pasillo, string fila, string columna)
        {
            int con = new[] { pasillo, fila, columna }.Count(t => Limpio(t) != null);
            return con == 0 || con == 3 ? null : "Del hueco hacen falta el pasillo, la fila y la columna.";
        }

        public static HuecoUbicacion Hueco(string pasillo, string fila, string columna) =>
            Limpio(pasillo) == null ? null : HuecoUbicacion.De(pasillo, fila, columna);

        /// <summary>Los productos existen, no son ficticios y, si el donante lleva número de serie, viene (LA REGLA del número de serie).</summary>
        public static List<string> MotivosProductos(SacarPiezaDTO peticion, ProductoIncompletoLeido donante, ProductoIncompletoLeido pieza)
        {
            var motivos = new List<string>();
            if (donante == null)
            {
                motivos.Add($"El producto {Codigo(peticion?.ProductoDonante)} no existe.");
            }
            else if (donante.Ficticio)
            {
                motivos.Add($"El {donante.Producto} es un producto ficticio: no tiene unidades de las que sacar piezas.");
            }
            if (pieza == null)
            {
                motivos.Add($"La pieza {Codigo(peticion?.Pieza)} no existe como producto: dela de alta antes de sacarla.");
            }
            else if (pieza.Ficticio)
            {
                motivos.Add($"La pieza {pieza.Producto} es un producto ficticio: no lleva stock.");
            }
            string motivoSerie = MotivoNumeroSerie(donante, peticion?.NumeroSerie);
            if (motivoSerie != null)
            {
                motivos.Add(motivoSerie);
            }
            return motivos;
        }

        /// <summary>Si el donante necesita número de serie (Productos.NecesitaNumSerie), es obligatorio: leído o tecleado.</summary>
        public static string MotivoNumeroSerie(ProductoIncompletoLeido donante, string numeroSerie)
        {
            return donante != null && donante.NecesitaNumSerie && Limpio(numeroSerie) == null
                ? $"El {donante.Producto} ({donante.Nombre?.Trim()}) lleva número de serie: léalo o tecléelo para saber qué unidad queda incompleta."
                : null;
        }

        public static string MotivoBajaNoValida(BajaProductoIncompletoDTO peticion)
        {
            string motivo = Limpio(peticion?.Motivo);
            if (motivo == null)
            {
                return "Diga el motivo de la baja (por qué no se va a completar).";
            }
            return motivo.Length > LONGITUD_COMENTARIO ? $"El motivo no puede pasar de {LONGITUD_COMENTARIO} caracteres." : null;
        }

        public static string MotivoCompletarNoValido(CompletarProductoIncompletoDTO peticion)
        {
            if ((Limpio(peticion?.Comentario)?.Length ?? 0) > LONGITUD_COMENTARIO)
            {
                return $"El comentario no puede pasar de {LONGITUD_COMENTARIO} caracteres.";
            }
            return MotivoHuecoNoValido(peticion?.Pasillo, peticion?.Fila, peticion?.Columna);
        }

        /// <summary>Solo se cierra (completar o dar de baja) lo que sigue incompleto. Null si se puede; si no, el motivo (409).</summary>
        public static string MotivoNoSePuedeCerrar(ProductoIncompletoDTO ficha)
        {
            if (ficha == null || ficha.EstaViva)
            {
                return null;
            }
            string cuando = ficha.FechaCierre.HasValue ? " el " + ficha.FechaCierre.Value.ToString("dd/MM/yyyy", es) : string.Empty;
            return string.Equals(ficha.Estado, EstadosProductoIncompleto.COMPLETADO, StringComparison.OrdinalIgnoreCase)
                ? $"La ficha {ficha.Id} ya se completó{cuando}."
                : $"La ficha {ficha.Id} ya se dio de baja{cuando}.";
        }

        // ---- La unidad

        /// <summary>
        /// La MISMA unidad física: mismo donante y mismo número de serie (no vacío). Sin número de serie no se sabe cuál es: cada ficha
        /// es su propia unidad.
        /// </summary>
        public static bool MismaUnidad(ProductoIncompletoDTO ficha, string donante, string numeroSerie)
        {
            return ficha != null && Limpio(numeroSerie) != null && Igual(ficha.ProductoDonante, donante) && Igual(ficha.NumeroSerie, numeroSerie);
        }

        /// <summary>La ficha viva de esa misma unidad (otra pieza que ya le falta), o null: entonces la unidad se aparta al sacar.</summary>
        public static ProductoIncompletoDTO UnidadYaApartada(IEnumerable<ProductoIncompletoDTO> vivas, string donante, string numeroSerie) =>
            (vivas ?? Enumerable.Empty<ProductoIncompletoDTO>()).Where(f => f != null && f.EstaViva && MismaUnidad(f, donante, numeroSerie))
                .OrderBy(f => f.Id).FirstOrDefault();

        /// <summary>Las OTRAS fichas vivas de la misma unidad que <paramref name="ficha"/>.</summary>
        public static List<ProductoIncompletoDTO> OtrasDeLaUnidad(ProductoIncompletoDTO ficha, IEnumerable<ProductoIncompletoDTO> vivas) =>
            (vivas ?? Enumerable.Empty<ProductoIncompletoDTO>())
                .Where(f => f != null && ficha != null && f.Id != ficha.Id && f.EstaViva && MismaUnidad(f, ficha.ProductoDonante, ficha.NumeroSerie))
                .OrderBy(f => f.Id).ToList();

        /// <summary>
        /// Al sacar otra pieza de una unidad que ya está apartada, tiene que ser del mismo almacén (la unidad está en la zona de incompletos de
        /// ese almacén). Null si vale.
        /// </summary>
        public static string MotivoUnidadEnOtroAlmacen(ProductoIncompletoDTO apartada, string almacen)
        {
            return apartada == null || Igual(apartada.Almacen, almacen)
                ? null
                : $"La unidad nº de serie {apartada.NumeroSerie} del {apartada.ProductoDonante} ya está incompleta en {apartada.Almacen} (ficha {apartada.Id}): saque la pieza allí.";
        }

        /// <summary>No hay en el almacén lo que se va a sacar (empresas 1 y 3). Null si hay.</summary>
        public static string MotivoSinStock(string producto, string almacen, int necesarias, IDictionary<string, int> stock, bool permiteNegativo, string queEs)
        {
            int hay = stock != null && producto != null && stock.TryGetValue(producto.Trim(), out int s) ? s : 0;
            if (permiteNegativo || hay >= necesarias)
            {
                return null;
            }
            return $"En {Codigo(almacen)} hay {hay} de {producto?.Trim()} y hacen falta {necesarias} ({queEs}).";
        }

        // ---- Los apuntes de stock

        /// <summary>
        /// SACAR, en un solo traspaso del diario <see cref="DIARIO"/>: +N de la pieza en el almacén y, si la unidad no estaba ya apartada,
        /// −1 del donante en el almacén y +1 en el de incompletos.
        /// </summary>
        public static List<LineaMovimientoTraspaso> LineasSacar(string almacen, string almacenIncompletos, string donante, string numeroSerie,
            string pieza, int cantidad, bool moverUnidad)
        {
            string serie = Limpio(numeroSerie) == null ? string.Empty : " NS " + Limpio(numeroSerie);
            var lineas = new List<LineaMovimientoTraspaso>
            {
                new LineaMovimientoTraspaso { Almacen = Codigo(almacen), Producto = Limpio(pieza), Cantidad = cantidad, Texto = Corto($"Pieza sacada del {Limpio(donante)}{serie}") }
            };
            if (moverUnidad)
            {
                lineas.Add(new LineaMovimientoTraspaso { Almacen = Codigo(almacen), Producto = Limpio(donante), Cantidad = -1, Texto = Corto($"Incompleto: le falta {Limpio(pieza)}{serie}") });
                lineas.Add(new LineaMovimientoTraspaso { Almacen = Codigo(almacenIncompletos), Producto = Limpio(donante), Cantidad = 1, Texto = Corto($"Incompleto: le falta {Limpio(pieza)}{serie}") });
            }
            return lineas;
        }

        /// <summary>COMPLETAR (diario <see cref="DIARIO"/>): −N de la pieza en el almacén y, si ya no le falta nada más, la unidad vuelve (−1 incompletos, +1 almacén).</summary>
        public static List<LineaMovimientoTraspaso> LineasCompletar(ProductoIncompletoDTO ficha, bool vuelveUnidad)
        {
            string serie = Limpio(ficha.NumeroSerie) == null ? string.Empty : " NS " + Limpio(ficha.NumeroSerie);
            var lineas = new List<LineaMovimientoTraspaso>
            {
                new LineaMovimientoTraspaso { Almacen = Codigo(ficha.Almacen), Producto = Limpio(ficha.Pieza), Cantidad = -ficha.Cantidad, Texto = Corto($"Pieza puesta al {Limpio(ficha.ProductoDonante)}{serie}") }
            };
            if (vuelveUnidad)
            {
                lineas.Add(new LineaMovimientoTraspaso { Almacen = Codigo(ficha.AlmacenIncompletos), Producto = Limpio(ficha.ProductoDonante), Cantidad = -1, Texto = Corto($"Completado (ficha {ficha.Id}){serie}") });
                lineas.Add(new LineaMovimientoTraspaso { Almacen = Codigo(ficha.Almacen), Producto = Limpio(ficha.ProductoDonante), Cantidad = 1, Texto = Corto($"Completado (ficha {ficha.Id}){serie}") });
            }
            return lineas;
        }

        /// <summary>
        /// BAJA (diario <see cref="DIARIO_BAJA"/>): −1 del donante en el almacén de incompletos. Es una merma con causa conocida: la unidad
        /// se tira o se queda para piezas. La pieza que se sacó ya entró (y se vendió) al sacarla: no se toca.
        /// </summary>
        public static List<LineaMovimientoTraspaso> LineasBaja(ProductoIncompletoDTO ficha, string motivo)
        {
            return new List<LineaMovimientoTraspaso>
            {
                new LineaMovimientoTraspaso { Almacen = Codigo(ficha.AlmacenIncompletos), Producto = Limpio(ficha.ProductoDonante), Cantidad = -1, Texto = Corto($"Baja incompleto: {Limpio(motivo)}") }
            };
        }

        public static List<ApunteProductoIncompletoDTO> Apuntes(string diario, IEnumerable<LineaMovimientoTraspaso> lineas) =>
            (lineas ?? Enumerable.Empty<LineaMovimientoTraspaso>()).Select(l => new ApunteProductoIncompletoDTO
            {
                Diario = diario, Almacen = l.Almacen, Producto = l.Producto, Cantidad = l.Cantidad, Texto = l.Texto
            }).ToList();

        private static string Corto(string texto) => texto == null ? null : (texto.Length > 50 ? texto.Substring(0, 50) : texto);

        // ---- Textos

        public static string Nombre(string producto, string nombre) =>
            Limpio(nombre) == null ? Limpio(producto) : $"{Limpio(producto)} {Limpio(nombre)}";

        public static string Unidades(int n) => n == 1 ? "1 ud." : n.ToString(CultureInfo.InvariantCulture) + " uds.";

        /// <summary>«En INC, hueco 002/002/004 (salió de ALG)» / «Completado: de vuelta en ALG» / «Dado de baja».</summary>
        public static string Donde(ProductoIncompletoDTO ficha)
        {
            if (ficha == null)
            {
                return null;
            }
            if (string.Equals(ficha.Estado, EstadosProductoIncompleto.COMPLETADO, StringComparison.OrdinalIgnoreCase))
            {
                return $"Completado: de vuelta en {Codigo(ficha.Almacen)}";
            }
            if (string.Equals(ficha.Estado, EstadosProductoIncompleto.BAJA, StringComparison.OrdinalIgnoreCase))
            {
                return "Dado de baja";
            }
            string hueco = Limpio(ficha.HuecoDestino) == null ? string.Empty : $", hueco {ficha.HuecoDestino.Trim()}";
            return $"En {Codigo(ficha.AlmacenIncompletos)}{hueco} (zona de incompletos de {Codigo(ficha.Almacen)})";
        }

        /// <summary>Días desde que se sacó la pieza (hasta el cierre si ya está cerrada).</summary>
        public static int Dias(ProductoIncompletoDTO ficha, DateTime hoy)
        {
            if (ficha == null || ficha.Fecha == default(DateTime))
            {
                return 0;
            }
            DateTime hasta = ficha.EstaViva || !ficha.FechaCierre.HasValue ? hoy : ficha.FechaCierre.Value;
            return Math.Max(0, (int)(hasta.Date - ficha.Fecha.Date).TotalDays);
        }

        /// <summary>Completa lo calculado de cada ficha leída.</summary>
        public static ProductoIncompletoDTO Completar(ProductoIncompletoDTO ficha, DateTime hoy)
        {
            if (ficha == null)
            {
                return null;
            }
            ficha.Dias = Dias(ficha, hoy);
            ficha.Donde = Donde(ficha);
            return ficha;
        }

        /// <summary>El listado: fichas y unidades distintas vivas con su valor a PVP (dos piezas de la misma serie = una unidad).</summary>
        public static ListadoProductosIncompletosDTO Listado(IEnumerable<ProductoIncompletoDTO> fichas, DateTime hoy)
        {
            List<ProductoIncompletoDTO> lista = (fichas ?? Enumerable.Empty<ProductoIncompletoDTO>()).Where(f => f != null)
                .Select(f => Completar(f, hoy)).OrderBy(f => f.EstaViva ? 0 : 1).ThenBy(f => f.Fecha).ThenBy(f => f.Id).ToList();
            var unidades = new List<ProductoIncompletoDTO>();
            foreach (ProductoIncompletoDTO viva in lista.Where(f => f.EstaViva))
            {
                if (!unidades.Any(u => MismaUnidad(u, viva.ProductoDonante, viva.NumeroSerie)))
                {
                    unidades.Add(viva);
                }
            }
            return new ListadoProductosIncompletosDTO
            {
                Fichas = lista,
                UnidadesIncompletas = unidades.Count,
                ValorPvp = unidades.Sum(u => u.PvpDonante ?? 0)
            };
        }

        /// <summary>La campana a Compras: «Se debe 1 ud. de 43415 CABLE… al 40252 DEPIBLEND nº de serie 101270».</summary>
        public static NotificacionPushDTO NotificacionCompras(ProductoIncompletoDTO ficha)
        {
            string serie = Limpio(ficha.NumeroSerie) == null ? string.Empty : $" nº de serie {ficha.NumeroSerie.Trim()}";
            string pedido = ficha.Pedido.HasValue ? $" Se sacó para el pedido {ficha.Pedido.Value.ToString(CultureInfo.InvariantCulture)}." : string.Empty;
            return new NotificacionPushDTO
            {
                Titulo = $"Pieza que se debe: {Limpio(ficha.Pieza)}",
                Cuerpo = $"Se debe {Unidades(ficha.Cantidad)} de {Nombre(ficha.Pieza, ficha.NombrePieza)} al {Nombre(ficha.ProductoDonante, ficha.NombreDonante)}{serie} " +
                    $"({Codigo(ficha.Almacen)}, ficha {ficha.Id}).{pedido} Inclúyala en el próximo pedido al proveedor para completarlo.",
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["id"] = ficha.Id.ToString(CultureInfo.InvariantCulture),
                    ["producto"] = Limpio(ficha.ProductoDonante),
                    ["pieza"] = Limpio(ficha.Pieza)
                }
            };
        }

        public static string MensajeSacar(ProductoIncompletoDTO ficha, bool ensayo, bool unidadMovida, int pendientesDeUbicar, string hueco)
        {
            var texto = new StringBuilder();
            texto.Append(ensayo ? "Se sacaría " : "Sacada ").Append(Unidades(ficha.Cantidad)).Append(" de ").Append(Limpio(ficha.Pieza))
                .Append(" del ").Append(Limpio(ficha.ProductoDonante));
            if (Limpio(ficha.NumeroSerie) != null)
            {
                texto.Append(" nº de serie ").Append(ficha.NumeroSerie.Trim());
            }
            texto.Append(" en ").Append(Codigo(ficha.Almacen)).Append(". ");
            if (unidadMovida)
            {
                texto.Append(ensayo ? "La unidad pasaría" : "La unidad pasa").Append(" a ").Append(Codigo(ficha.AlmacenIncompletos))
                    .Append(" y deja de contar como disponible");
                if (hueco != null)
                {
                    texto.Append(" (sale del hueco ").Append(hueco).Append(')');
                }
                texto.Append(". ");
            }
            else
            {
                texto.Append("La unidad ya estaba apartada como incompleta. ");
            }
            if (pendientesDeUbicar > 0)
            {
                texto.Append(ensayo ? "La pieza quedaría" : "La pieza queda").Append(" pendiente de ubicar.");
            }
            return texto.ToString().Trim();
        }

        public static string MensajeCompletar(ProductoIncompletoDTO ficha, bool ensayo, bool vuelveUnidad, int otrasPiezas)
        {
            string quien = $"El {Limpio(ficha.ProductoDonante)}" + (Limpio(ficha.NumeroSerie) == null ? string.Empty : $" nº de serie {ficha.NumeroSerie.Trim()}");
            string pieza = $"{Unidades(ficha.Cantidad)} de {Limpio(ficha.Pieza)}";
            if (!vuelveUnidad)
            {
                return ensayo
                    ? $"Se pondría {pieza}. {quien} seguiría incompleto: le faltan {otrasPiezas} pieza(s) más."
                    : $"Puesta {pieza}. {quien} sigue incompleto: le faltan {otrasPiezas} pieza(s) más.";
            }
            return ensayo
                ? $"Se pondría {pieza} y {quien} volvería a {Codigo(ficha.Almacen)} como disponible."
                : $"Puesta {pieza}. {quien} vuelve a {Codigo(ficha.Almacen)} como disponible" +
                  (EsAlgete(ficha.Almacen) ? ", pendiente de ubicar." : ".");
        }

        public static string MensajeBaja(ProductoIncompletoDTO ficha, bool ensayo, int fichasCerradas)
        {
            string quien = $"el {Limpio(ficha.ProductoDonante)}" + (Limpio(ficha.NumeroSerie) == null ? string.Empty : $" nº de serie {ficha.NumeroSerie.Trim()}");
            string otras = fichasCerradas > 1 ? $" (se cierran sus {fichasCerradas} fichas)" : string.Empty;
            return ensayo
                ? $"Se daría de baja {quien}: saldría de {Codigo(ficha.AlmacenIncompletos)}{otras}."
                : $"Dado de baja {quien}: sale de {Codigo(ficha.AlmacenIncompletos)}{otras}.";
        }

        public static bool EsAlgete(string almacen) => Igual(almacen, Constantes.Almacenes.ALGETE);
    }

    // ---------------------------------------------------------------- La etiqueta «INCOMPLETO»

    /// <summary>
    /// NestoAPI#902: la etiqueta que se pega en la unidad incompleta, en la MISMA 3×2 (30×20 mm) y la misma impresora que las de producto
    /// (ImpresoraCodBarras del usuario). Arriba «INCOMPLETO» en grande; debajo, el producto y su número de serie, la pieza que falta, la
    /// fecha y el pedido; abajo, un Code128 «INC{ficha}» para que Ariadna la lea al completarla. Constantes para ajustarlas tras la prueba.
    /// </summary>
    public static class GeneradorEtiquetaIncompleto
    {
        public const string CABECERA = "INCOMPLETO";
        public const int CABECERA_Y = 4;
        public const int TEXTO_Y = 34;
        public const int INTERLINEA = 14;
        public const int LINEAS = 4;
        /// <summary>34 + 4 × 14 = 90; 6 puntos de aire.</summary>
        public const int CODIGO_Y = 96;
        public const int CODIGO_ALTO = 44;
        public const int CODIGO_ESTRECHO = 2;

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>Las líneas pequeñas (27 caracteres): «Ref. 40252 NS 101270», «DEPIBLEND…», «Falta: 1 43415 CABLE…», «24/11/26 · Pedido 931421».</summary>
        public static List<string> Lineas(ProductoIncompletoDTO ficha)
        {
            int ancho = GeneradorEtiquetasProducto.CARACTERES_PEQUENA;
            string serie = ReglasProductosIncompletos.Limpio(ficha?.NumeroSerie);
            var lineas = new List<string>
            {
                Recortar("Ref. " + ReglasProductosIncompletos.Limpio(ficha?.ProductoDonante) + (serie == null ? string.Empty : " NS " + serie), ancho),
                Recortar(GeneradorEtiquetasProducto.Partir(ficha?.NombreDonante, ancho).FirstOrDefault() ?? string.Empty, ancho),
                Recortar($"Falta: {ficha?.Cantidad ?? 0} {ReglasProductosIncompletos.Nombre(ficha?.Pieza, ficha?.NombrePieza)}", ancho),
                Recortar((ficha?.Fecha ?? DateTime.Today).ToString("dd/MM/yy", es) +
                    (ficha?.Pedido.HasValue == true ? " · Pedido " + ficha.Pedido.Value.ToString(CultureInfo.InvariantCulture) : string.Empty), ancho)
            };
            return lineas.Where(l => !string.IsNullOrWhiteSpace(l)).Take(LINEAS).ToList();
        }

        public static string Epl(ProductoIncompletoDTO ficha)
        {
            var epl = new StringBuilder();
            epl.Append("I8,A,034\r\n");
            epl.Append("q").Append(GeneradorEtiquetasHueco.ANCHO).Append("\r\n");
            epl.Append("Q").Append(GeneradorEtiquetasHueco.ALTO).Append(",").Append(GeneradorEtiquetasHueco.SEPARACION).Append("\r\n");
            epl.Append("N\r\n");
            epl.Append(string.Format(CultureInfo.InvariantCulture, "A{0},{1},0,{2},1,1,N,\"{3}\"\r\n",
                GeneradorEtiquetasProducto.MARGEN_X, CABECERA_Y, GeneradorEtiquetasProducto.FUENTE_REFERENCIA_EPL, CABECERA));
            List<string> lineas = Lineas(ficha);
            for (int i = 0; i < lineas.Count; i++)
            {
                epl.Append(string.Format(CultureInfo.InvariantCulture, "A{0},{1},0,{2},1,1,N,\"{3}\"\r\n",
                    GeneradorEtiquetasProducto.MARGEN_X, TEXTO_Y + i * INTERLINEA, GeneradorEtiquetasProducto.FUENTE_PEQUENA, TextoEpl(lineas[i])));
            }
            epl.Append(string.Format(CultureInfo.InvariantCulture, "B{0},{1},0,1,{2},{2},{3},N,\"{4}\"\r\n",
                GeneradorEtiquetasProducto.MARGEN_X, CODIGO_Y, CODIGO_ESTRECHO, CODIGO_ALTO, ficha.CodigoEtiqueta));
            epl.Append("P1\r\n");
            return epl.ToString();
        }

        public static string Zpl(ProductoIncompletoDTO ficha)
        {
            var zpl = new StringBuilder();
            zpl.Append(ComandosZpl.Inicio());
            zpl.Append(ComandosZpl.TextoEscalable(GeneradorEtiquetasProducto.MARGEN_X, CABECERA_Y, GeneradorEtiquetasProducto.REFERENCIA_ALTO,
                GeneradorEtiquetasProducto.REFERENCIA_ANCHO, CABECERA));
            List<string> lineas = Lineas(ficha);
            for (int i = 0; i < lineas.Count; i++)
            {
                zpl.Append(ComandosZpl.Texto(GeneradorEtiquetasProducto.MARGEN_X, TEXTO_Y + i * INTERLINEA, GeneradorEtiquetasProducto.FUENTE_PEQUENA, lineas[i]));
            }
            zpl.Append(ComandosZpl.Codigo128(GeneradorEtiquetasProducto.MARGEN_X, CODIGO_Y, CODIGO_ESTRECHO, CODIGO_ALTO, ficha.CodigoEtiqueta));
            zpl.Append(ComandosZpl.Fin(1));
            return zpl.ToString();
        }

        public static TrabajoEtiquetas Trabajo(ProductoIncompletoDTO ficha) =>
            new TrabajoEtiquetas(Epl(ficha), Zpl(ficha), $"Etiqueta INCOMPLETO {ficha?.ProductoDonante?.Trim()} (ficha {ficha?.Id})");

        private static string Recortar(string texto, int ancho)
        {
            string sinControl = new string((texto ?? string.Empty).Where(c => !char.IsControl(c)).ToArray());
            string limpio = string.Join(" ", sinControl.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return limpio.Length > ancho ? limpio.Substring(0, ancho).TrimEnd() : limpio;
        }

        private static string TextoEpl(string texto) => (texto ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
