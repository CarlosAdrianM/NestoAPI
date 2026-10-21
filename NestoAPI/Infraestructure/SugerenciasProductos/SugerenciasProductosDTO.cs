using System;
using System.Collections.Generic;
using FuentesNucleo = ModeloSugerenciasProductos.Reglas.Fuentes;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>NestoAPI#670: de dónde sale una sugerencia (va tal cual en la columna Motivo). NestoAPI#671: los del núcleo.</summary>
    public static class MotivosSugerencia
    {
        public const string COMPRADO_JUNTO = FuentesNucleo.COMPRADO_JUNTO;
        public const string LE_TOCA = FuentesNucleo.LE_TOCA;
        public const string POTENCIAR = FuentesNucleo.POTENCIAR;
        /// <summary>NestoAPI#671 (corte 2a): item-kNN por coseno; lo compran clientes que compran lo mismo que este.</summary>
        public const string PARECIDOS = FuentesNucleo.PARECIDOS;
    }

    /// <summary>
    /// NestoAPI#670: lo que contesta el vendedor dentro de la sugerencia («✓ Añadir», «✗ No le interesa» y, desde Nesto#537
    /// segunda vuelta, «🚫 Quitar»).
    /// <para>IMPORTANTE para el modelo de aceptación (NestoAPI#671, corte 2e): solo <see cref="ANADIDO"/> (positivo) y
    /// <see cref="NO_LE_INTERESA"/> (negativo) son respuestas DEL CLIENTE. <see cref="DESCARTADO_VENDEDOR"/> es una decisión
    /// del vendedor sin llegar a ofrecerlo («sé que no lo quiere» o «no se lo puedo vender»): NO es un negativo del cliente y
    /// no debe entrar como etiqueta 0 al entrenar; como mucho, como dato aparte (o se descarta la fila). Lo mismo con las
    /// filas provisionales de antes del script Issue670b (Respuesta «NoLeInteresa» con Texto que empieza por
    /// <see cref="MARCA_DESCARTADO_VENDEDOR"/>): el script las pasa a «DescartadoVendedor».</para>
    /// <para>Para no volver a sugerirlo a ese cliente en 60 días, «NoLeInteresa» y «DescartadoVendedor» cuentan igual
    /// (<see cref="ExcluyenSesentaDias"/>).</para>
    /// </summary>
    public static class RespuestasSugerencia
    {
        public const string ANADIDO = "Anadido";
        public const string NO_LE_INTERESA = "NoLeInteresa";
        /// <summary>Nesto#537: «No se lo ofrezco» (el vendedor la quita sin ofrecerla). No es un rechazo del cliente.</summary>
        public const string DESCARTADO_VENDEDOR = "DescartadoVendedor";

        /// <summary>
        /// Sin el script Issue670b, el CK de Respuesta no admite «DescartadoVendedor»: se guarda «NoLeInteresa» (excluye igual
        /// 60 días) y el Texto empieza por esta marca, para que el script lo pase a «DescartadoVendedor» y el modelo no lo lea
        /// como un rechazo del cliente.
        /// </summary>
        public const string MARCA_DESCARTADO_VENDEDOR = "[DescartadoVendedor] ";

        /// <summary>
        /// NestoAPI#682: la API la quitó del panel sin que nadie la tocara, porque con el carrito nuevo salió otra claramente
        /// mejor (la sustituta ocupa su hueco). NO es una respuesta del vendedor ni del cliente: ni positivo ni negativo para
        /// ningún modelo (como una sin respuesta: no se llegó a ofrecer), no excluye 60 días y no vuelve a salir en esa sesión.
        /// No la manda ningún cliente (Responder no la admite).
        /// </summary>
        public const string SUSTITUIDA = "Sustituida";

        /// <summary>
        /// NestoAPI#682: sin el script Issue682 el CK de Respuesta no admite «Sustituida»: la fila se queda con Respuesta NULL
        /// (como una no tocada, que tampoco cuenta) y el Texto empieza por esta marca, para no volver a enseñarla. El script la
        /// pasa a «Sustituida».
        /// </summary>
        public const string MARCA_SUSTITUIDA = "[Sustituida] ";

        /// <summary>Las que no se vuelven a sugerir a ese cliente en 60 días y dejan hueco para una sustituta.</summary>
        public static bool ExcluyenSesentaDias(string respuesta)
            => respuesta == NO_LE_INTERESA || respuesta == DESCARTADO_VENDEDOR;

        /// <summary>NestoAPI#682: sustituida antes del script Issue682 (Respuesta NULL con la marca en el Texto).</summary>
        public static bool EsSustituidaProvisional(string respuesta, string texto)
            => respuesta == null && texto != null && texto.StartsWith(MARCA_SUSTITUIDA, StringComparison.Ordinal);

        /// <summary>
        /// NestoAPI#669: «🚫 Quitar» del vendedor, también la provisional de antes del script Issue670b («NoLeInteresa» con
        /// <see cref="MARCA_DESCARTADO_VENDEDOR"/> delante del Texto).
        /// </summary>
        public static bool EsQuitada(string respuesta, string texto)
            => respuesta == DESCARTADO_VENDEDOR
               || (respuesta == NO_LE_INTERESA && texto != null && texto.StartsWith(MARCA_DESCARTADO_VENDEDOR, StringComparison.Ordinal));
    }

    /// <summary>
    /// NestoAPI#670/#669: un producto que no se le vuelve a sugerir a un cliente («No le interesa» o «🚫 Quitar» en los últimos
    /// 60 días), con su familia, para que dos «Quitar» de la misma familia la excluyan entera.
    /// </summary>
    public class RechazoCliente
    {
        public string Producto { get; set; }
        /// <summary>Productos.Familia (código). Null si no hay ficha.</summary>
        public string Familia { get; set; }
        /// <summary>Si fue «🚫 Quitar» (DescartadoVendedor, también la provisional), no «No le interesa».</summary>
        public bool Quitado { get; set; }
    }

    /// <summary>NestoAPI#670: aplicaciones que pueden pedir sugerencias.</summary>
    public static class AplicacionesSugerencias
    {
        public const string NESTO = "Nesto";
        public const string NESTO_APP = "NestoApp";
    }

    /// <summary>NestoAPI#670: qué productos ve el vendedor (épica #669: peluquería al de peluquería, estética al de estética).</summary>
    public enum FiltroGrupoVendedor
    {
        /// <summary>Sin restricción (vendedor sin indicar, vendedor general o el mismo vendedor lleva las dos cosas del cliente).</summary>
        Todos,
        /// <summary>Todo menos el grupo PEL.</summary>
        Estetica,
        /// <summary>Solo el grupo PEL.</summary>
        Peluqueria
    }

    public class ProductoCarritoSugerenciaDTO
    {
        public string Producto { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>NestoAPI#670: cuerpo de POST api/SugerenciasProductos/Calcular.</summary>
    public class CalcularSugerenciasDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Vendedor { get; set; }
        /// <summary>El Guid de la plantilla. Vacío o null: sesión nueva (se devuelve en la respuesta).</summary>
        public Guid? IdSesion { get; set; }
        /// <summary>"Nesto" o "NestoApp".</summary>
        public string Aplicacion { get; set; }
        public List<ProductoCarritoSugerenciaDTO> Productos { get; set; } = new List<ProductoCarritoSugerenciaDTO>();
    }

    public class SugerenciaProductoDTO
    {
        public int IdSugerencia { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>"CompradoJunto", "LeToca", "Potenciar" o "Parecidos".</summary>
        public string Motivo { get; set; }
        /// <summary>La frase que puede decir el vendedor («Lo compra cada ~45 días; el último, hace 52.»).</summary>
        public string Texto { get; set; }
        /// <summary>1..4: el hueco del panel (la sustituta ocupa el de la rechazada).</summary>
        public int Posicion { get; set; }

        // Nesto#537 (segunda vuelta): fichas más ricas. Opcionales: los clientes de antes no los leen.

        /// <summary>La foto de la tienda (la misma de Ariadna, con caché). Null si no tiene o la tienda no contesta a tiempo.</summary>
        public string UrlFoto { get; set; }
        /// <summary>Productos.Tamaño. Null o 0: sin tamaño.</summary>
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        /// <summary>La descripción de la familia (Familias.Descripción); si no la tiene, el código.</summary>
        public string Familia { get; set; }
        /// <summary>«Crema Reafirmante 500 ml»: el nombre con el tamaño y la unidad detrás (sin tamaño, el nombre solo).</summary>
        public string NombreConTamano { get; set; }
    }

    /// <summary>NestoAPI#670: respuesta de Calcular.</summary>
    public class SugerenciasProductosDTO
    {
        public Guid IdSesion { get; set; }
        public List<SugerenciaProductoDTO> Sugerencias { get; set; } = new List<SugerenciaProductoDTO>();
    }

    /// <summary>NestoAPI#670: cuerpo de POST api/SugerenciasProductos/{IdSugerencia}/Respuesta.</summary>
    public class RespuestaSugerenciaDTO
    {
        /// <summary>"Anadido", "NoLeInteresa" o "DescartadoVendedor".</summary>
        public string Respuesta { get; set; }
        /// <summary>Opcional: el carrito de ahora. Si no viene, se usa el del último Calcular de la sesión.</summary>
        public List<ProductoCarritoSugerenciaDTO> Productos { get; set; }
    }

    /// <summary>NestoAPI#670: respuesta de .../Respuesta.</summary>
    public class ResultadoRespuestaSugerenciaDTO
    {
        public int IdSugerencia { get; set; }
        public string Respuesta { get; set; }
        /// <summary>Con «NoLeInteresa» o «DescartadoVendedor», la que ocupa su hueco. Null si no hay otra (o con «Anadido»).</summary>
        public SugerenciaProductoDTO Sustituta { get; set; }
        /// <summary>Las que quedan visibles después de responder (con la sustituta).</summary>
        public List<SugerenciaProductoDTO> Sugerencias { get; set; } = new List<SugerenciaProductoDTO>();
    }

    /// <summary>NestoAPI#670: petición que no vale (400) o sugerencia que no existe (404).</summary>
    public class SugerenciasProductosException : Exception
    {
        public SugerenciasProductosException(bool noEncontrada, string mensaje) : base(mensaje)
        {
            NoEncontrada = noEncontrada;
        }

        public bool NoEncontrada { get; }
    }

    // ---------- Lo que se lee de la BD (fuera del EDMX) ----------

    public class SesionSugerencias
    {
        public Guid Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Vendedor { get; set; }
        public string Usuario { get; set; }
        public string Aplicacion { get; set; }
        public DateTime FechaInicio { get; set; }
        public string Carrito { get; set; }
        public int? Pedido { get; set; }
    }

    public class SugerenciaMostrada
    {
        public int Id { get; set; }
        public Guid SesionId { get; set; }
        public string Producto { get; set; }
        public string Motivo { get; set; }
        public decimal Puntuacion { get; set; }
        public byte Posicion { get; set; }
        public string VersionMotor { get; set; }
        public string Texto { get; set; }
        public DateTime FechaMostrada { get; set; }
        public string Respuesta { get; set; }
        public DateTime? FechaRespuesta { get; set; }
        public bool? EntroEnPedido { get; set; }
        public int? CantidadEnPedido { get; set; }
        /// <summary>NestoAPI#671: todas las fuentes que propusieron el producto («CompradoJunto,Parecidos»). Solo se escribe.</summary>
        public string Fuentes { get; set; }
        /// <summary>NestoAPI#671: productos distintos del carrito cuando se enseñó. Solo se escribe.</summary>
        public int? TamanoCarrito { get; set; }
        /// <summary>NestoAPI#671: hueco elegido al azar (ε-greedy, corte 2c). Hasta el 2c, siempre false.</summary>
        public bool Exploracion { get; set; }
    }

    /// <summary>
    /// NestoAPI#671 (corte 2b): hereda todo (Producto, Nombre, Grupo, SubGrupo, Familia, Estado, Ficticio) de la ficha del
    /// núcleo, para que las reglas del núcleo miren las mismas fichas sin copiarlas.
    /// </summary>
    public class FichaProductoSugerencia : ModeloSugerenciasProductos.Datos.FichaProducto
    {
        /// <summary>Nesto#537: Productos.Tamaño (solo para enseñarlo).</summary>
        public short? Tamano { get; set; }
        /// <summary>Nesto#537: Productos.UnidadMedida (solo para enseñarlo).</summary>
        public string UnidadMedida { get; set; }
        /// <summary>Nesto#537: Familias.Descripción (solo para enseñarlo; las reglas miran el código, <c>Familia</c>).</summary>
        public string NombreFamilia { get; set; }
    }

    public class ParCompradoJunto
    {
        public string ProductoA { get; set; }
        public string ProductoB { get; set; }
        public int VecesJuntos { get; set; }
        public decimal Soporte { get; set; }
        public decimal Confianza { get; set; }
        public decimal Lift { get; set; }
    }

    public class ProductoPerfil
    {
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public string Producto { get; set; }
        public int Clientes { get; set; }
    }

    /// <summary>NestoAPI#670: un producto que se quiere empujar (oferta activa, «Potenciar hasta» o novedad).</summary>
    public class CandidatoPotenciar
    {
        public const string TIPO_OFERTA = "Oferta";
        public const string TIPO_POTENCIAR = "Potenciar";
        public const string TIPO_NOVEDAD = "Novedad";

        public string Producto { get; set; }
        public string Tipo { get; set; }
        /// <summary>Para las ofertas, «6+2». Null si no hay detalle.</summary>
        public string Detalle { get; set; }
    }

    /// <summary>NestoAPI#670: datos del vendedor con los que se decide su grupo.</summary>
    public class DatosGrupoVendedor
    {
        /// <summary>Vendedores.Estado de la ficha del vendedor. Null si no se encuentra.</summary>
        public short? EstadoVendedor { get; set; }
        /// <summary>Clientes.Vendedor del cliente (el de estética).</summary>
        public string VendedorEsteticaCliente { get; set; }
        /// <summary>VendedoresClienteGrupoProducto con grupo PEL del cliente.</summary>
        public string VendedorPeluqueriaCliente { get; set; }
    }

    /// <summary>NestoAPI#671: una fila de SugerenciasProductosAfinidad (item-kNN de un cliente con un producto que no compra).</summary>
    public class AfinidadCliente
    {
        public string Cliente { get; set; }
        public string Producto { get; set; }
        public decimal Afinidad { get; set; }
        public short Rango { get; set; }
    }

    /// <summary>NestoAPI#671: una fila de SugerenciasProductosPopularidad.</summary>
    public class PopularidadProducto
    {
        public string Producto { get; set; }
        public int Clientes12Meses { get; set; }
        public int Clientes3Meses { get; set; }
        public int Cestas12Meses { get; set; }
        public int Rango { get; set; }
    }

    /// <summary>NestoAPI#670: una sugerencia posible, antes de enseñarla.</summary>
    public class CandidatoSugerencia
    {
        public string Producto { get; set; }
        public string Motivo { get; set; }
        public decimal Puntuacion { get; set; }
        public string Texto { get; set; }
    }
}
