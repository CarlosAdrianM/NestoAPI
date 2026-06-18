using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// Un intercambio crudo con la API de la agencia: la petición y la respuesta tal cual viajaron
    /// (para Innovatrans, los sobres SOAP). Sirve para auditar/depurar, sobre todo el primer envío
    /// real (no hay entorno de pruebas).
    /// </summary>
    public class IntercambioRemoto
    {
        public string Operacion { get; set; }
        public string Url { get; set; }
        public string Peticion { get; set; }
        public string Respuesta { get; set; }
    }

    /// <summary>
    /// Acumula los intercambios crudos que hace una agencia remota durante una operación (p.ej.
    /// tramitar = insertar + etiquetar = 2 intercambios). El cliente de la API escribe aquí; la
    /// estrategia lo expone para que el llamante (controller) los guarde en la auditoría.
    /// </summary>
    public class RegistroIntercambiosRemotos
    {
        private readonly List<IntercambioRemoto> _intercambios = new List<IntercambioRemoto>();

        public void Registrar(string operacion, string url, string peticion, string respuesta)
        {
            _intercambios.Add(new IntercambioRemoto
            {
                Operacion = operacion,
                Url = url,
                Peticion = peticion,
                Respuesta = respuesta
            });
        }

        public IReadOnlyList<IntercambioRemoto> Intercambios => _intercambios;
    }
}
