using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Clientes
{
    // NestoAPI#819: los mensajes de los límites van en castellano; MensajesValidacionCliente los completa con la longitud real.
    public class ClienteCrear
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string CodigoPostal { get; set; }
        public string Comentarios { get; set; }
        [StringLength(50, ErrorMessage = "El comentario de picking no puede superar {1} caracteres.")]
        public string ComentariosPicking { get; set; }
        [StringLength(50, ErrorMessage = "El comentario de ruta no puede superar {1} caracteres.")]
        public string ComentariosRuta { get; set; }
        [StringLength(50, ErrorMessage = "La dirección no puede superar {1} caracteres.")]
        public string Direccion { get; set; }
        /// <summary>
        /// NestoAPI#499: la dirección la eligió el usuario de entre las que propone Google (Nesto#480,
        /// NestoApp#180), no la tecleó. Con el parámetro ExigirDireccionVerificadaAlta encendido, un
        /// alta con dirección y este flag a false se rechaza. Los clientes que no lo mandan lo dejan
        /// en false: por eso el interruptor nace apagado.
        /// </summary>
        public bool DireccionVerificada { get; set; }
        /// <summary>
        /// NestoAPI#874: el número de la calle cuando Google dio la calle SIN número (cifras o «S/N»). Ya va dentro de
        /// <see cref="Direccion"/> (lo pone ComprobarDatosGenerales); aquí es para que el alta lo vuelva a comprobar con Google.
        /// Null si Google dio el número (lo normal).
        /// </summary>
        [StringLength(10, ErrorMessage = "El número de la calle no puede superar {1} caracteres.")]
        public string Numero { get; set; }
        /// <summary>
        /// NestoAPI#874: el usuario ha confirmado un <see cref="Numero"/> de portal válido que Google no encuentra en esa calle y CP
        /// (ComprobarDatosGenerales devolvió NumeroSinConfirmarPorGoogle). Sin esto, ese caso es un 400 con la pregunta.
        /// </summary>
        public bool NumeroConfirmadoSinGoogle { get; set; }
        /// <summary>
        /// NestoAPI#874: «¿Qué es?» la dirección: Piso, Local, Casa, Nave u Otro (<see cref="Infraestructure.Clientes.TiposLugar"/>).
        /// Con los campos de su tipo compone Clientes.Direccion2 (<see cref="Infraestructure.Clientes.Direccion2Cliente"/>).
        /// </summary>
        public string TipoLugar { get; set; }
        /// <summary>NestoAPI#874, Piso: el piso («1», «BAJO»). Obligatorio con Puerta.</summary>
        public string Piso { get; set; }
        /// <summary>NestoAPI#874, Piso: la puerta («D»).</summary>
        public string Puerta { get; set; }
        /// <summary>NestoAPI#874, Local: número de local (opcional).</summary>
        public string NumeroLocal { get; set; }
        /// <summary>NestoAPI#874, Local: lo que pone en el rótulo (obligatorio; no se propone el nombre fiscal).</summary>
        public string Rotulo { get; set; }
        /// <summary>NestoAPI#874, Nave: número de la nave (obligatorio).</summary>
        public string NumeroNave { get; set; }
        /// <summary>NestoAPI#874, Otro: el texto (obligatorio).</summary>
        public string OtroTexto { get; set; }
        /// <summary>
        /// NestoAPI#874 (corte 2), solo al MODIFICAR: borrar la Dirección 2 guardada. Sin tipo de lugar y sin esto, la que hay se
        /// conserva (NestoApp y Nesto anteriores no mandan los campos).
        /// </summary>
        public bool QuitarDireccion2 { get; set; }
        /// <summary>NestoAPI#874 (corte 2): solo lectura (GET GetClienteCrear): la Dirección 2 guardada tal cual. El PUT la ignora.</summary>
        public string Direccion2 { get; set; }
        public bool EsContacto {get;set;}
        public short? Estado { get; set; }
        public bool Estetica { get; set; }
        /// <summary>
        /// Nesto#555 (sugerencia 770): alta de una ALUMNA de cursos y eventos. La API la graba con Estado 10 (Alumna) y
        /// vendedor NV, ignorando Estado, Estetica, Peluqueria y los vendedores que vengan. Excluyente con <see cref="Publico"/>.
        /// </summary>
        public bool Alumna { get; set; }
        /// <summary>
        /// Nesto#555 (Laura, comentario 411 de la 770): alta de PÚBLICO FINAL (profesional de otro sector que pide factura).
        /// La API la graba con Estado 8 (público final) y vendedor NV. Excluyente con <see cref="Alumna"/>.
        /// </summary>
        public bool Publico { get; set; }
        public string FormaPago { get; set; }
        public string Iban { get; set; }
        public string Nif {get;set;}
        /// <summary>NestoAPI#355: país del cliente (ISO-2). Null/vacío = ES (default de la casa).
        /// Para país ≠ ES el NIF puede ser un NIF-IVA intracomunitario (varchar(20), #356).</summary>
        [StringLength(2, ErrorMessage = "El país no puede superar {1} caracteres (código ISO de dos letras).")]
        public string Pais { get; set; }
        [StringLength(50, ErrorMessage = "El nombre fiscal no puede superar {1} caracteres.")]
        public string Nombre { get; set; }
        public bool Peluqueria { get; set; }
        public string PlazosPago { get; set; }
        [StringLength(50, ErrorMessage = "La población no puede superar {1} caracteres.")]
        public string Poblacion { get; set; }
        [StringLength(50, ErrorMessage = "La provincia no puede superar {1} caracteres.")]
        public string Provincia { get; set; }
        public string Ruta { get; set; }
        [StringLength(29, ErrorMessage = "El teléfono no puede superar {1} caracteres.")]
        public string Telefono { get; set; }
        public string VendedorEstetica { get; set; }
        public string VendedorPeluqueria { get; set; }
        // NestoAPI#471: días que el cliente cierra (5 posiciones L..V, '1' abre). Null en el PUT = no tocar.
        public string DiasEnServir { get; set; }
        /// <summary>
        /// NestoAPI#541: si el cambio de DiasEnServir cierra un día y el contacto tiene pedidos con picking, el PUT
        /// se rechaza (código DIAS_CON_PICKING) hasta que el usuario acepte avisar a almacén: entonces se repite
        /// con esto a true, se guarda y se manda el correo. Los clientes que no lo mandan reciben el rechazo.
        /// </summary>
        public bool ConfirmarDiasEnServirConPicking { get; set; }

        public string Usuario { get; set; }

        /// <summary>
        /// NestoAPI#716: en PUT api/Clientes/DejarDeVisitar, por qué se deja de visitar (un código de
        /// MotivoCambioEstadoCliente; fuera del catálogo es un 400). Opcional: sin él la API anota «DejarDeVisitar».
        /// </summary>
        public string MotivoCambioEstado { get; set; }
        /// <summary>NestoAPI#716: texto libre que acompaña al motivo (200 caracteres como mucho). Opcional.</summary>
        public string ComentarioCambioEstado { get; set; }
        /// <summary>
        /// NestoAPI#716: en el POST (alta), desde qué aplicación se da de alta (Clientes.AplicacionAlta, 20 caracteres).
        /// Opcional: si no viene, la API lo deduce de cómo se autenticó quien llama (Nesto, NestoApp, TiendaOnline).
        /// </summary>
        public string AplicacionAlta { get; set; }
        /// <summary>
        /// NestoAPI#623: código del catálogo de orígenes (GET api/Clientes/Origenes), p. ej. «META-HARMONIA-0926». Opcional: null = no
        /// se toca (en el alta, sin origen); en el alta se graba si viene; en la modificación, si viene distinto. Para quitarlo,
        /// PUT api/Clientes/{empresa}/{cliente}/Origen con Origen null. Va por cliente (todos sus contactos), en la tabla
        /// ClientesOrigen. 400 si no existe o no está activo. GET api/Clientes/GetClienteCrear lo trae relleno.
        /// </summary>
        public string Origen { get; set; }

        /// <summary>
        /// NestoAPI#876: con Origen «CAMBIO-FISCAL» (cliente que ya lo era y cambia de razón social o NIF), el número de cliente que
        /// tenía antes: obligatorio con ese origen, tiene que existir y no puede ser él mismo (400 de usted); sin ese origen, 400. Solo
        /// en el alta (dbo.ClientesAnteriores; sin la tabla no se exige ni se guarda). GET api/Clientes/GetClienteCrear lo trae relleno.
        /// Comercialmente es el mismo cliente; nada fiscal se hereda.
        /// </summary>
        public string ClienteAnterior { get; set; }

        /// <summary>
        /// Alta con dirección de entrega distinta (19/11/26). Opcional y solo en el alta de un cliente NUEVO (con <see cref="EsContacto"/>
        /// es un 400): crea además un segundo contacto con esta dirección y deja el ContactoDefecto del principal apuntándole. Sin él,
        /// el alta es exactamente la de siempre. La respuesta trae el contacto creado en Cliente.ContactoEntrega.
        /// </summary>
        public DireccionEntregaCrear DireccionEntrega { get; set; }

        /// <summary>
        /// NestoAPI#876: con <see cref="ClienteAnterior"/>, los bloques que se copian de su ficha tras crear la nueva
        /// (<see cref="BloquesCambioFiscal"/>; lista vacía = todos). Null = no se copia nada (como hoy). Si la copia falla, el alta
        /// queda hecha y la respuesta lo dice en Cliente.AvisoCopiaClienteAnterior.
        /// </summary>
        public List<string> CopiarDelAnterior { get; set; }


        public virtual ICollection<PersonaContactoDTO> PersonasContacto { get; set; }
    }
}