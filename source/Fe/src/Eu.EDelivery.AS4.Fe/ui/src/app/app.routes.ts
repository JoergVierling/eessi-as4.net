import { tokenNotExpired } from 'angular2-jwt';
import { Injectable } from '@angular/core';
import { Routes, RouterModule, CanActivate, Router } from '@angular/router';

import { NoContentComponent } from './no-content';

import { WrapperComponent } from './common/wrapper.component';
import { MustBeAuthorizedGuard } from './common/mustbeauthorized.guards';
import { AgentSettingsComponent } from './settings/agent/agent.component';

export const ROUTES: Routes = [
    { path: '**', component: NoContentComponent }
];