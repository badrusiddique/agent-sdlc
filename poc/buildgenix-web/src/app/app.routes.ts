import { Routes } from '@angular/router';
import { StudioPageComponent } from './studio/studio-page.component';

export const routes: Routes = [
  { path: '', component: StudioPageComponent },
  { path: '**', redirectTo: '' },
];
