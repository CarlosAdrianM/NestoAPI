using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>NestoAPI#670: de dónde sale una sugerencia (va tal cual en la columna Motivo).</summary>
    public static class MotivosSugerencia
    {
        public const string COMPRADO_JUNTO = "CompradoJunto";
        public const string LE_TOCA = "LeToca";
        public const string POTENCIAR = "Potenciar";
    }

    /// <summary>NestoAPI#670: lo que contesta el vendedor dentro de la sugerencia («✓ Añadir» y «✗ No le interesa»).</summary>
    public static class RespuestasSugerencia
    {
        public const string ANADIDO = "Anadido";
        public const string NO_LE_INTERESA = "NoLeInteresa";
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
        /// <summary>"CompradoJunto", "LeToca" o "Potenciar".</summary>
        public string Motivo { get; set; }
        /// <summary>La frase que puede decir el vendedor («Lo compra cada ~45 días; el último, hace 52.»).</summary>
        public string Texto { get; set; }
        /// <summary>1..4: el hueco del panel (la sustituta ocupa el de la rechazada).</summary>
        public int Posicion { get; set; }
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
        /// <summary>"Anadido" o "NoLeInteresa".</summary>
        public string Respuesta { get; set; }
        /// <summary>Opcional: el carrito de ahora. Si no viene, se usa el del último Calcular de la sesión.</summary>
        public List<ProductoCarritoSugerenciaDTO> Productos { get; set; }
    }

    /// <summary>NestoAPI#670: respuesta de .../Respuesta.</summary>
    public class ResultadoRespuestaSugerenciaDTO
    {
        public int IdSugerencia { get; set; }
        public string Respuesta { get; set; }
        /// <summary>Con «NoLeInteresa», la que ocupa su hueco. Null si no hay otra (o con «Anadido»).</summary>
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
    }

    public class FichaProductoSugerencia
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public string Familia { get; set; }
        public short Estado { get; set; }
        public bool Ficticio { get; set; }
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

    /// <summary>NestoAPI#670: una sugerencia posible, antes de enseñarla.</summary>
    public class CandidatoSugerencia
    {
        public string Producto { get; set; }
        public string Motivo { get; set; }
        public decimal Puntuacion { get; set; }
        public string Texto { get; set; }
    }
}
