using NestoAPI.Models.OfertasCombinadas;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.OfertasAutorizadas
{
    /// <summary>
    /// NestoAPI#233: lo que cambia de una pestaña del mantenimiento de ofertas a otra (combinadas,
    /// por familia, escalonadas). El núcleo (<see cref="ServicioOfertasAutorizadas"/>) monta la
    /// notificación y la lectura agregada; cada estrategia solo sabe leer su tabla y contar su
    /// oferta en una frase.
    /// </summary>
    internal interface ITipoOfertaAutorizada
    {
        /// <summary>Clave del tipo en la ruta y en los datos de la push: combinada, familia o escalonada (contrato con NestoApp#137).</summary>
        string Tipo { get; }

        /// <summary>El resumen de la oferta para el cuerpo de la push, o null si la oferta no existe.</summary>
        Task<AvisoOfertaAutorizada> ConstruirAviso(int id);

        /// <summary>Añade al resultado las ofertas de este tipo vigentes en <paramref name="hoy"/>.</summary>
        Task AnadirVigentes(string empresa, DateTime hoy, OfertasAutorizadasDTO destino);
    }

    internal class AvisoOfertaAutorizada
    {
        public string Cuerpo { get; set; }

        /// <summary>Si no es null, de esta oferta no se avisa y este es el porqué (p. ej. es una denegación).</summary>
        public string MotivoNoSeAvisa { get; set; }
    }
}
