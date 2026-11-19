using System.ComponentModel.DataAnnotations;

namespace NestoAPI.Models.Clientes
{
    /// <summary>
    /// Alta con dirección de entrega distinta (19/11/26): la segunda dirección del alta de un cliente NUEVO. Con ella, POST api/Clientes
    /// crea en una sola transacción el contacto principal (dirección fiscal, la de <see cref="ClienteCrear"/>) y un segundo contacto con
    /// esta dirección, al que apunta el ContactoDefecto del principal (la entrega por defecto). Lo demás (NIF, nombre, estado, forma y
    /// plazos de pago, IBAN, vendedores, días de servir, personas de contacto) lo hereda del principal. Mismos nombres y límites que los
    /// campos de dirección de <see cref="ClienteCrear"/>; las reglas son las mismas (Google, número propio y «¿Qué es?» de NestoAPI#874).
    /// </summary>
    public class DireccionEntregaCrear
    {
        [StringLength(50, ErrorMessage = "La dirección no puede superar {1} caracteres.")]
        public string Direccion { get; set; }
        /// <summary>La eligió el usuario de las que propone Google (como <see cref="ClienteCrear.DireccionVerificada"/>).</summary>
        public bool DireccionVerificada { get; set; }
        [StringLength(10, ErrorMessage = "El número de la calle no puede superar {1} caracteres.")]
        public string Numero { get; set; }
        public bool NumeroConfirmadoSinGoogle { get; set; }
        public string TipoLugar { get; set; }
        public string Piso { get; set; }
        public string Puerta { get; set; }
        public string NumeroLocal { get; set; }
        public string Rotulo { get; set; }
        public string NumeroNave { get; set; }
        public string OtroTexto { get; set; }
        public string CodigoPostal { get; set; }
        [StringLength(50, ErrorMessage = "La población no puede superar {1} caracteres.")]
        public string Poblacion { get; set; }
        [StringLength(50, ErrorMessage = "La provincia no puede superar {1} caracteres.")]
        public string Provincia { get; set; }
        /// <summary>La ruta del código postal de ESTA dirección (la da ComprobarDatosGenerales).</summary>
        public string Ruta { get; set; }
        [StringLength(29, ErrorMessage = "El teléfono no puede superar {1} caracteres.")]
        public string Telefono { get; set; }
        [StringLength(50, ErrorMessage = "El comentario de ruta no puede superar {1} caracteres.")]
        public string ComentariosRuta { get; set; }
        [StringLength(50, ErrorMessage = "El comentario de picking no puede superar {1} caracteres.")]
        public string ComentariosPicking { get; set; }
    }
}
