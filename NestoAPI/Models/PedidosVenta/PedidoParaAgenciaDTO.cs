using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// Nesto#340 (slice A3): el pedido tal y como lo necesita el módulo de Agencias de Nesto, para
    /// que deje de cargar la entidad EF <c>CabPedidoVta</c> con sus Includes.
    ///
    /// ⚠️ AQUÍ NO SE HACE Trim A PROPÓSITO. Agencias compara estos campos SIN Trim contra listas
    /// que todavía vienen de EF con el padding de la BD (<c>"1  "</c>): p. ej.
    /// <c>listaEmpresas.Single(e =&gt; e.Número = pedido.Empresa)</c> y el equivalente de agencias.
    /// Si aquí se recortara, esos Single dejarían de encontrar nada y romperían la selección de
    /// empresa y de agencia. El cliente ya hace su propio Trim donde le hace falta. La paridad
    /// byte a byte con lo que devolvía EF es el contrato de este DTO.
    /// </summary>
    public class PedidoParaAgenciaDTO
    {
        public string Empresa { get; set; }
        public int Numero { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public DateTime? Fecha { get; set; }
        public string Vendedor { get; set; }
        public string Comentarios { get; set; }
        public string ComentarioPicking { get; set; }

        /// <summary>
        /// Ficha del cliente. Puede ser null, y ESO IMPORTA: Agencias usa "sin ficha" como
        /// señal de pedido no utilizable y revierte al pedido anterior. No sustituir por un
        /// objeto vacío.
        /// </summary>
        public ClienteParaAgenciaDTO ClienteFicha { get; set; }
    }

    public class ClienteParaAgenciaDTO
    {
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Telefono { get; set; }

        /// <summary>
        /// Nunca null: Agencias hace .Any() y .ToList() sin comprobarlo (con EF era un HashSet
        /// vacío). Se devuelven TODAS las personas de contacto, sin filtrar por cargo: el
        /// criterio de elección del correo vive en el cliente (CorreoCliente.CorreoAgencia) y
        /// ahí se queda.
        /// </summary>
        public List<PersonaContactoAgenciaDTO> PersonasContacto { get; set; } = new List<PersonaContactoAgenciaDTO>();
    }

    public class PersonaContactoAgenciaDTO
    {
        public short Cargo { get; set; }
        public string CorreoElectronico { get; set; }
    }
}
