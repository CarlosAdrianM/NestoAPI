//using NestoAPI.Models.Kits;
//using System.Collections.Generic;
//using System.Threading.Tasks;

//namespace NestoAPI.Infraestructure.Kits
//{
//    public class GestorPreExtractoProducto
//    {
//        private List<PreExtractoProductoDTO> _preExtractos;
//        private readonly IUbicacionService _servicioUbicacion;

//        // ESTA CLASE CREO QUE NO VALE PARA NADA PORQUE ES LO MISMO QUE GESTORKITS
//        // ELIMINAR DEL PROYECTO


//        public GestorPreExtractoProducto(List<PreExtractoProductoDTO> preExtractos, IUbicacionService servicioUbicacion)
//        {
//            _servicioUbicacion = servicioUbicacion;

//            // Llamada al método asíncrono en el constructor no es recomendada
//            // Para evitar problemas, almacenamos la tarea y esperamos en otro método
//            InicializarAsync();
//        }

//        private async void InicializarAsync()
//        {
//            // Llamamos al método asíncrono y esperamos su resultado
//            await AsignarUbicacionesMasAntiguasAsync();
//        }

//        private async Task AsignarUbicacionesMasAntiguasAsync()
//        {
//            GestorUbicaciones gestorUbicaciones = new GestorUbicaciones(_servicioUbicacion);
//            _preExtractos = await gestorUbicaciones.AsignarUbicacionesMasAntiguas(_preExtractos);
//        }

//        public void Guardar()
//        {
//            // Implementa la lógica para guardar los PreExtrProducto en la base de datos
//            // Puedes acceder a PreExtractosAsignados para obtener los resultados ya asignados
//        }
//    }
//}