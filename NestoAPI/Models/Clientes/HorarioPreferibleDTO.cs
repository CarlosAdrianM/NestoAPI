using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Clientes
{
    // NestoAPI#763 (Nesto#584): el «horario preferible» de entrega de un cliente/contacto. JSON en PascalCase.

    /// <summary>Una franja del horario preferible: un día de la semana y de qué hora a qué hora.</summary>
    public class FranjaHorarioPreferibleDTO
    {
        /// <summary>1 = lunes … 7 = domingo.</summary>
        public int DiaSemana { get; set; }
        /// <summary>«HH:mm».</summary>
        public string Desde { get; set; }
        /// <summary>«HH:mm», mayor que <see cref="Desde"/>.</summary>
        public string Hasta { get; set; }
    }

    /// <summary>
    /// GET/PUT api/Clientes/{empresa}/{cliente}/{contacto}/HorarioPreferible. Para el PUT basta con <see cref="Franjas"/> (o, si no
    /// vienen, <see cref="Texto"/> con el formato «L-V 10:00-14:00, 17:00-20:00; S 10:00-13:00»). Sin franjas, se borra el horario.
    /// </summary>
    public class HorarioPreferibleDTO
    {
        /// <summary>Lo que hay que enseñar siempre junto al horario.</summary>
        public const string AVISO = "Horario preferible del cliente: lo intentaremos, pero no hay compromiso de respetarlo.";

        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>Ordenadas por día y hora. Vacía si el cliente no tiene horario preferible.</summary>
        public List<FranjaHorarioPreferibleDTO> Franjas { get; set; } = new List<FranjaHorarioPreferibleDTO>();
        /// <summary>El horario en texto («L-V 10:00-14:00, 17:00-20:00; S 10:00-13:00»); vacío si no tiene.</summary>
        public string Texto { get; set; }
        /// <summary>Siempre <see cref="AVISO"/>.</summary>
        public string Aviso { get; set; } = AVISO;
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
        /// <summary>La propuesta sacada de los comentarios que está por revisar para este contacto, o null.</summary>
        public PropuestaHorarioPreferibleDTO PropuestaPendiente { get; set; }
    }

    /// <summary>
    /// GET api/Clientes/HorariosPreferibles/Propuestas: un horario sacado de los comentarios de la ficha que espera a que una
    /// persona lo revise (Nesto). Hasta que se acepta, no cuenta para nada.
    /// </summary>
    public class PropuestaHorarioPreferibleDTO
    {
        public const string ESTADO_POR_REVISAR = "PorRevisar";
        public const string ESTADO_ACEPTADA = "Aceptada";
        public const string ESTADO_DESCARTADA = "Descartada";

        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string Poblacion { get; set; }
        public string Ruta { get; set; }
        /// <summary>El texto de los comentarios del que sale la propuesta.</summary>
        public string TextoOrigen { get; set; }
        /// <summary>El horario que proponen las reglas, en texto; null si no han sabido sacarlo (hay que escribirlo a mano).</summary>
        public string HorarioPropuesto { get; set; }
        public List<FranjaHorarioPreferibleDTO> Franjas { get; set; } = new List<FranjaHorarioPreferibleDTO>();
        public string Estado { get; set; }
        public DateTime Fecha { get; set; }
    }
}
