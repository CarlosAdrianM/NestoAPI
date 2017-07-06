using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class GestorFestivos :IGestorDiasNoLaborables
    {
        public GestorFestivos()
        {
            ListaFestivos = new List<Festivo>();
        }
        public List<Festivo> ListaFestivos { get; set; }
        public void Rellenar(DateTime fechaHasta, DateTime fechaDesde)
        {
            Festivo festivo1 = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 1),
                Fiesta = "Fiesta del Trabajo",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo1);
            Festivo festivo2 = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 2),
                Fiesta = "Fiesta de la Comunidad de Madrid",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo2);
            Festivo festivo3 = new Festivo()
            {
                Fecha = new DateTime(2017, 8, 15),
                Fiesta = "Asunción de la Virgen",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo3);
            Festivo festivo4 = new Festivo()
            {
                Fecha = new DateTime(2017, 10, 12),
                Fiesta = "Fiesta Nacional",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo4);
            Festivo festivo5 = new Festivo()
            {
                Fecha = new DateTime(2017, 11, 1),
                Fiesta = "Todos los Santos",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo5);
            Festivo festivo6 = new Festivo()
            {
                Fecha = new DateTime(2017, 12, 6),
                Fiesta = "Día de la Constitución",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo6);
            Festivo festivo7 = new Festivo()
            {
                Fecha = new DateTime(2017, 12, 8),
                Fiesta = "Día de la Inmaculada Concepción",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo7);
            Festivo festivo8 = new Festivo()
            {
                Fecha = new DateTime(2017, 12, 25),
                Fiesta = "Natividad del Señor",
                TipoFestivo = TipoFestivo.Todas
            };
            ListaFestivos.Add(festivo8);

            // ALGETE
            Festivo festivo9 = new Festivo()
            {
                Fecha = new DateTime(2017, 9, 8),
                Fiesta = "Fiesta local Algete",
                TipoFestivo = TipoFestivo.Algete
            };
            ListaFestivos.Add(festivo9);
            Festivo festivo10 = new Festivo()
            {
                Fecha = new DateTime(2017, 9, 11),
                Fiesta = "Fiesta local Algete",
                TipoFestivo = TipoFestivo.Algete
            };
            ListaFestivos.Add(festivo10);

            //MADRID
            Festivo festivo11 = new Festivo()
            {
                Fecha = new DateTime(2017, 5, 15),
                Fiesta = "Fiesta local Madrid (San Isidro)",
                TipoFestivo = TipoFestivo.Madrid
            };
            ListaFestivos.Add(festivo11);
            Festivo festivo12 = new Festivo()
            {
                Fecha = new DateTime(2017, 11, 9),
                Fiesta = "Fiesta local Madrid (Almudena)",
                TipoFestivo = TipoFestivo.Madrid
            };
            ListaFestivos.Add(festivo12);
        }
    }
}